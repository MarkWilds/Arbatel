﻿using Arbatel.Formats.Quake;
using Arbatel.Graphics;
using Arbatel.Utilities;
using OpenTK;
using OpenTK.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace Arbatel.Formats
{
	public static class QuakeMapObjectExtensions
	{
		public static List<MapObject> Collapse(this MapObject mo)
		{
			var collapsed = new QuakeMapObject(mo);
			collapsed.Children.Clear();

			foreach (MapObject child in mo.Children)
			{
				collapsed.Children.AddRange(new QuakeMapObject(child).Collapse());
			}

			if (mo.Definition.ClassName == "worldspawn")
			{
				collapsed.Saveability = Saveability.Solids;
			}
			else if (mo.Definition.ClassName == "func_instance")
			{
				collapsed.Saveability = Saveability.Children;
			}

			bool onlyChildren = collapsed.Saveability == Saveability.Children;
			return onlyChildren ? collapsed.Children : new List<MapObject>() { collapsed };
		}
	}

	/// <summary>
	/// Any key/value-bearing entity in a Quake map.
	/// </summary>
	public class QuakeMapObject : MapObject
	{
		public QuakeMapObject() : base()
		{
		}
		public QuakeMapObject(MapObject mo) : base(mo)
		{
		}
		public QuakeMapObject(QuakeMapObject qmo) : base(qmo)
		{
		}
		public QuakeMapObject(Block block, DefinitionDictionary definitions) :
			this(block, definitions, new TextureDictionary())
		{
		}
		public QuakeMapObject(Block block, DefinitionDictionary definitions, TextureDictionary textures) :
			base(block, definitions, textures)
		{
			QuakeBlock quakeBlock;
			if (block is QuakeBlock)
			{
				quakeBlock = block as QuakeBlock;
			}
			else
			{
				throw new ArgumentException("Provided Block isn't actually a QuakeBlock!");
			}

			KeyVals = new Dictionary<string, Option>(quakeBlock.KeyVals);

			if (definitions.ContainsKey(KeyVals["classname"].Value))
			{
				Definition = definitions[KeyVals["classname"].Value];
			}
			else
			{
				Definition = new Definition();
			}

			Saveability = Definition.Saveability;

			TextureCollection = textures;

			foreach (Block child in quakeBlock.Children)
			{
				if (child.KeyVals.Count > 0)
				{
					Children.Add(new QuakeMapObject(child, definitions, textures));
				}
				else
				{
					ExtractRenderables(child);
				}
			}

			ExtractRenderables(quakeBlock);

			UpdateBounds();

			Position = Aabb.Center;

			if (KeyVals.ContainsKey("origin"))
			{
				Position = KeyVals["origin"].Value.ToVector3();
			}
			else if (Definition.ClassName == "worldspawn")
			{
				Position = new Vector3(0, 0, 0);
			}
		}

		protected override void ExtractRenderables(Block block)
		{
			var b = block as QuakeBlock;

			// Contains brushes. Checking the Solids count allows for both known
			// and unknown solid entities, which can be treated the same way.
			if (b.Solids.Count > 0)
			{
				foreach (Solid solid in b.Solids)
				{
					Renderables.Add(new QuakeBrush(solid));
				}
			}
			// Known point entity.
			else if (Definition?.ClassType == ClassType.Point)
			{
				float x = Position.X;
				float y = Position.Y;
				float z = Position.Z;

				if (KeyVals.ContainsKey("origin"))
				{
					Position = KeyVals["origin"].Value.ToVector3();
				}

				if (Definition.RenderableSources.ContainsKey(RenderableSource.Key))
				{
					string key = Definition.RenderableSources[RenderableSource.Key];

					string path = KeyVals[key].Value;

					if (path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
					{
						string oldCwd = Directory.GetCurrentDirectory();
						string instancePath = oldCwd + Path.DirectorySeparatorChar + path;

						QuakeMap map;
						using (FileStream stream = File.OpenRead(instancePath))
						{
							map = new QuakeMap(stream, Definition.DefinitionCollection);
						}
						map.Transform(this);
						UserData = map;

						foreach (MapObject mo in map.AllObjects)
						{
							var modified = new QuakeMapObject(mo);
							if (mo.KeyVals["classname"].Value == "worldspawn")
							{
								modified.Saveability = Saveability.Solids;
							}

							Children.Add(modified);
						}

						// Create a simple box to mark this instance's origin.
						Renderable box = new BoxGenerator(Color4.Orange).Generate();
						box.Position = new Vector3(x, y, z);
						box.Transformability = Definition.RenderableTransformability;

						Renderables.Add(box);
					}
				}
				else if (Definition.RenderableSources.ContainsKey(RenderableSource.Model))
				{
					LoadModel(block as QuakeBlock);
				}
				else if (Definition.RenderableSources.ContainsKey(RenderableSource.Size))
				{
					Aabb s = Definition.Size;

					Renderable box = new BoxGenerator(s.Min, s.Max, Definition.Color).Generate();

					box.Position = Position;

					Renderables.Add(box);
				}
				// Known point entity with no predefined size.
				else
				{
					Renderable gem = new GemGenerator(Color4.Lime).Generate();

					gem.Position = new Vector3(x, y, z);
					gem.Transformability = Definition.RenderableTransformability;

					Renderables.Add(gem);
				}
			}
			// Unknown entity.
			else if (Definition == null)
			{
				Renderable gem = new GemGenerator().Generate();

				gem.Position = Position;
				gem.Transformability = Definition.RenderableTransformability;

				Renderables.Add(gem);
			}
		}

		public void LoadModel(QuakeBlock block)
		{
			Renderable gem = new GemGenerator(Color4.Red).Generate();

			gem.Position = block.KeyVals["origin"].Value.ToVector3();
			gem.Transformability = Definition.RenderableTransformability;

			Renderables.Add(gem);
		}
	}
}
