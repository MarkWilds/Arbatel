﻿using Eto.Gl;
using OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temblor.Graphics;

namespace Temblor.Formats
{
	public class MapObject
	{
		public Block Block;

		public List<MapObject> Children;

		public List<Renderable> Renderables;

		public MapObject() : this(new Block())
		{
		}
		public MapObject(Block _block)
		{
			Block = _block;

			Children = new List<MapObject>();

			Renderables = new List<Renderable>();
		}

		public void Draw(Shader shader, GLSurface surface)
		{
			foreach (var child in Children)
			{
				child.Draw(shader, surface);
			}

			foreach (var renderable in Renderables)
			{
				renderable.Draw(shader, surface);
			}
		}

		public void Init(GLSurface surface)
		{
			foreach (var child in Children)
			{
				child.Init(surface);
			}

			foreach (var renderable in Renderables)
			{
				renderable.Init(surface);
			}
		}

		virtual protected void ExtractRenderables(Block block)
		{
		}
	}
}