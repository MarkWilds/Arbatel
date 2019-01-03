﻿using Arbatel.UI;
using Arbatel.Utilities;
using Eto.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Arbatel.Formats.Quake
{
	public enum CollapseType
	{
		Position,
		Angle,
		Target
	}

	public class QuakeMap : Map
	{
		public QuakeMap() : base()
		{
		}
		public QuakeMap(QuakeMap map) : base(map)
		{
		}
		public QuakeMap(Stream stream, DefinitionDictionary definitions) : base(stream, definitions)
		{
			using (var sr = new StreamReader(stream))
			{
				Parse(sr.ReadToEnd());
			}
		}

		/// <summary>
		/// Update the Saveability of every MapObject in this map, setting
		/// instance entities to be Saveability.Children.
		/// </summary>
		/// <param name="map"></param>
		/// <returns>A copy of the input Map with its MapObjects' Saveability
		/// updated, in preparation for writing to disk as a string.</returns>
		public override Map Collapse()
		{
			var collapsed = new QuakeMap(this);
			collapsed.MapObjects.Clear();

			int worldspawnIndex = MapObjects.FindIndex(o => o.Definition.ClassName == "worldspawn");

			MapObject worldspawn = MapObjects[worldspawnIndex];

			collapsed.MapObjects.Add(worldspawn);

			for (int i = 0; i < MapObjects.Count; i++)
			{
				if (i != worldspawnIndex)
				{
					List<MapObject> collapsedObject = MapObjects[i].Collapse();

					foreach (MapObject co in collapsedObject)
					{
						if (co.Definition.ClassName == "worldspawn")
						{
							worldspawn.Renderables.AddRange(co.Renderables);
						}
						else
						{
							collapsed.MapObjects.Add(co);
						}
					}
				}
			}

			return collapsed;
		}

		public override string ToString()
		{
			return ToString(QuakeSideFormat.Valve220);
		}
		public string ToString(QuakeSideFormat format)
		{
			var sb = new StringBuilder();

			// First build the final worldspawn.
			int worldspawnIndex = MapObjects.FindIndex(o => o.Definition.ClassName == "worldspawn");
			MapObject worldspawn = MapObjects[worldspawnIndex];
			foreach (MapObject mo in AllObjects)
			{
				// Only copy solids to worldspawn if that's all this entity
				// is set to save, otherwise let it get handled later.
				if (mo.Saveability == Saveability.Solids)
				{
					worldspawn.Renderables.AddRange(mo.Renderables);
				}
			}

			sb.AppendLine(new QuakeBlock(worldspawn).ToString(format));

			for (int i = 0; i < MapObjects.Count; i++)
			{
				if (i == worldspawnIndex)
				{
					continue;
				}

				MapObject mo = MapObjects[i];

				// Avoid saving a null character to the output.
				if (mo.Saveability == Saveability.None)
				{
					continue;
				}

				sb.AppendLine(new QuakeBlock(mo).ToString(format));
			}

			return sb.ToString();
		}

		private void Parse(string raw)
		{
			string oldCwd = Directory.GetCurrentDirectory();
			Directory.SetCurrentDirectory(Path.GetDirectoryName(AbsolutePath ?? oldCwd));

			string stripped = Regex.Replace(raw, "//.*?[\r\n]", "");
			stripped = stripped.Replace("\r", String.Empty);
			stripped = stripped.Replace("\n", String.Empty);
			stripped = stripped.Replace("\t", String.Empty); // FIXME: Only strip leading tabs! Or just tabs not within a key or value?

			// Modern Quake sourceports allow for transparency in textures,
			// indicated by an open curly brace in the texture name. This
			// complicates the Regex necessary to split the map file. Any open
			// curly brace that's actually a delimiter will be followed by an
			// open parenthesis or one or more whitespace characters. Texture
			// names will instead have ordinary text characters after the brace.
			string curly = Regex.Escape(OpenDelimiter);

			// The whitespace here is outside the capture group, and will be
			// discarded, but it would get stripped later anyway.
			string whitespaceAfter = "(" + curly + ")\\s+";

			// These, however, are inside the capture group, and will be
			// preserved, but will end up in the same list item as the open
			// curly brace. A quick loop later on will clean that up.
			string parenthesisAfter = "(" + curly + "\\()";
			string quoteAfter = "(" + curly + "\")";

			string delimiters =
				whitespaceAfter + "|" +
				parenthesisAfter + "|" +
				quoteAfter + "|" +
				"(" + Regex.Escape(CloseDelimiter) + ")";

			var split = Regex.Split(stripped, delimiters).ToList();
			split.RemoveAll(s => s.Trim().Length == 0 || s.Trim() == "\"");

			// The regex patterns above include capture groups to retain some
			// delimiters in the output, but they end up in the same item in the
			// list resulting from Split. This ugly loop fixes that.
			int item = 0;
			while (item < split.Count)
			{
				if (split[item] == "{(")
				{
					split[item] = "{";
					split[item + 1] = "(" + split[item + 1];
					item++;
				}
				else if (split[item] == "{\"")
				{
					split[item] = "{";
					split[item + 1] = "\"" + split[item + 1];
					item++;
				}
				else
				{
					item++;
				}
			}

			int i = 0;
			while (i < split.Count)
			{
				int firstBraceIndex = split.IndexOf("{", i);

				var block = new QuakeBlock(split, firstBraceIndex, Definitions);
				MapObjects.Add(new QuakeMapObject(block, Definitions, Textures));

				i = firstBraceIndex + block.RawLength;
			}

			// First build the final worldspawn.
			int worldspawnIndex = MapObjects.FindIndex(o => o.Definition.ClassName == "worldspawn");
			MapObject worldspawn = MapObjects[worldspawnIndex];
			foreach (MapObject mo in AllObjects)
			{
				Aabb += mo.Aabb;
			}

			Directory.SetCurrentDirectory(oldCwd);
		}

		public override void UpdateFromSettings(Settings settings)
		{
			// TODO: Update entity definitions too. Will probably require
			// hanging on to a copy of the input map's raw string for reparsing,
			// or at least the path to the original file.

			var textures = new Dictionary<string, TextureDictionary>();

			Stream stream = null;
			if (settings.Local.UsingCustomPalette)
			{
				stream = File.OpenRead(settings.Local.LastCustomPalette.LocalPath);
			}
			else
			{
				string name = $"palette-{settings.Roaming.LastBuiltInPalette.ToLower()}.lmp";

				stream = Assembly.GetAssembly(typeof(MainForm)).GetResourceStream(name);
			}

			using (stream)
			{
				Palette palette = new Palette().LoadQuakePalette(stream);

				foreach (string path in settings.Local.TextureDictionaryPaths)
				{
					textures.Add(path, Loader.LoadTextureDictionary(path, palette));
				}
			}

			Textures = textures.Values.ToList().Stack();
		}
	}
}
