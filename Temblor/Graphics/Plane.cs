﻿using OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temblor.Utilities;

namespace Temblor.Graphics
{
	public class Plane
	{
		public readonly List<Vertex> Points;

		public readonly Vector3 Normal;

		public readonly float DistanceFromOrigin;

		public readonly Winding Winding;

		public Plane(Vector3 _a, Vector3 _b, Vector3 _c, Winding _winding) : this(new Vertex(_a), new Vertex(_b), new Vertex(_c), _winding)
		{
		}
		public Plane(Vertex _a, Vertex _b, Vertex _c, Winding _winding) : this(new List<Vertex>() { _a, _b, _c }, _winding)
		{
		}
		public Plane(List<Vertex> _points, Winding _winding)
		{
			Winding = _winding;

			Points = _points;

			var a = Points[2] - Points[0];
			var b = Points[1] - Points[0];

			if (Winding == Winding.CCW)
			{
				var c = a;
				a = b;
				b = c;
			}

			Normal = Vector3.Cross(a, b);
			Normal.Normalize();

			DistanceFromOrigin = Vector3.Dot(Points[0].Position, Normal);
		}

		public static Vector3 Intersect(Plane a, Plane b, Plane c)
		{
			var denominator = Vector3.Dot(a.Normal, Vector3.Cross(b.Normal, c.Normal));

			// Planes do not intersect.
			if (MathHelper.ApproximatelyEqualEpsilon(denominator, 0.0f, 0.001f))
			{
				return new Vector3(float.NaN, float.NaN, float.NaN);
			}

			var crossAB = Vector3.Cross(a.Normal, b.Normal);
			crossAB *= c.DistanceFromOrigin;

			var crossBC = Vector3.Cross(b.Normal, c.Normal);
			crossBC *= a.DistanceFromOrigin;

			var crossCA = Vector3.Cross(c.Normal, a.Normal);
			crossCA *= b.DistanceFromOrigin;

			return new Vector3(crossBC + crossCA + crossAB) / denominator;
		}
	}
}
