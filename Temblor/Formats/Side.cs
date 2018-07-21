﻿using OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temblor.Graphics;
using Temblor.Utilities;

namespace Temblor.Formats
{
	public class Side
	{
		public Plane Plane;

		public List<Vector3> TextureBasis;
		public string TextureName;
		public Vector2 TextureOffset;
		public float TextureRotation;
		public Vector2 TextureScale;

		public List<Vertex> Vertices;

		public Side()
		{
			TextureBasis = new List<Vector3>();
			TextureOffset = new Vector2();
			TextureScale = new Vector2();

			Vertices = new List<Vertex>();
		}
	}
}
