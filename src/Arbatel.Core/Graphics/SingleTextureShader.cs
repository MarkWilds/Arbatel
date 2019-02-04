﻿using Eto.Gl;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arbatel.UI;

namespace Arbatel.Graphics
{
	/// <summary>
	/// A shader that applies a single texture to a surface.
	/// </summary>
	public class SingleTextureShader : Shader
	{
		public string[] VertexShaderSource330 =
		{
			"#version 330 core",
			"layout (location = 0) in vec3 position;",
			"layout (location = 1) in vec3 normal;",
			"layout (location = 2) in vec4 color;",
			"",
			"out vec2 texCoords;",
			"",
			"uniform mat4 model;",
			"uniform mat4 view;",
			"uniform mat4 projection;",
			"uniform vec3 basisS;",
			"uniform vec3 basisT;",
			"uniform vec2 offset;",
			"uniform vec2 scale;",
			"uniform float textureWidth;",
			"uniform float textureHeight;",
			"",
			"void main()",
			"{",
			"	// Quake maps, like all clever, handsome developers, use",
			"	// left-handed, Z-up world coordinates. OpenGL, in contrast,",
			"	// uses right-handed, Y-up coordinates.",
			"	vec3 yUpRightHand = vec3(position.x, position.z, -position.y);",
			"   gl_Position = projection * view * model * vec4(yUpRightHand, 1.0f);",
			"",
			"	float coordS = (dot(position, basisS) + (offset.x * scale.x)) / (textureWidth * scale.x);",
			"	float coordT = (dot(position, basisT) + (offset.y * scale.y)) / (textureHeight * scale.y);",
			"",
			"	texCoords = vec2(coordS, coordT);",
			"}"
		};
		public string[] FragmentShaderSource330 =
		{
			"#version 330 core",
			"",
			"in vec2 texCoords;",
			"",
			"out vec4 color;",
			"",
			"uniform sampler2D tex;",
			"",
			"void main()",
			"{",
			"	color = texture(tex, texCoords);",
			"}"
		};

		public string[] VertexShaderSource120 =
		{
			"#version 120",
			"",
			"attribute vec3 position;",
			"attribute vec3 normal;",
			"attribute vec4 color;",
			"",
			"varying vec2 texCoords;",
			"",
			"uniform mat4 model;",
			"uniform mat4 view;",
			"uniform mat4 projection;",
			"uniform vec3 basisS;",
			"uniform vec3 basisT;",
			"uniform vec2 offset;",
			"uniform vec2 scale;",
			"uniform float textureWidth;",
			"uniform float textureHeight;",
			"",
			"void main()",
			"{",
			"	vec3 yUpRightHand = vec3(position.x, position.z, -position.y);",
			"	gl_Position = projection * view * model * vec4(yUpRightHand, 1.0f);",
			"",
			"	float coordS = (dot(position, basisS) + (offset.x * scale.x)) / (textureWidth * scale.x);",
			"	float coordT = (dot(position, basisT) + (offset.y * scale.y)) / (textureHeight * scale.y);",
			"",
			"	texCoords = vec2(coordS, coordT);",
			"}"
		};
		public string[] FragmentShaderSource120 =
		{
			"#version 120",
			"",
			"varying vec2 texCoords;",
			"",
			"uniform sampler2D tex;",
			"",
			"void main()",
			"{",
			"	gl_FragColor = texture2D(tex, texCoords);",
			"}"
		};

		public int LocationBasisS;
		public int LocationBasisT;
		public int LocationOffset;
		public int LocationScale;
		public int LocationTextureWidth;
		public int LocationTextureHeight;

		public SingleTextureShader() : base()
		{
			LocationBasisS = 0;
			LocationBasisT = 0;
			LocationOffset = 0;
			LocationScale = 0;
			LocationTextureWidth = 0;
			LocationTextureHeight = 0;
		}
		public SingleTextureShader(int major, int minor) : this()
		{
			if (major >= 3 && minor >= 3)
			{
				Compile(VertexShaderSource330, FragmentShaderSource330);
			}
			else
			{
				Compile(VertexShaderSource120, FragmentShaderSource120);
			}

			LocationModelMatrix = GL.GetUniformLocation(Program, "model");
			LocationViewMatrix = GL.GetUniformLocation(Program, "view");
			LocationProjectionMatrix = GL.GetUniformLocation(Program, "projection");

			LocationBasisS = GL.GetUniformLocation(Program, "basisS");
			LocationBasisT = GL.GetUniformLocation(Program, "basisT");
			LocationOffset = GL.GetUniformLocation(Program, "offset");
			LocationScale = GL.GetUniformLocation(Program, "scale");
			LocationTextureWidth = GL.GetUniformLocation(Program, "textureWidth");
			LocationTextureHeight = GL.GetUniformLocation(Program, "textureHeight");
		}

		private List<(Polygon, Renderable)> VisiblePolygons { get; } = new List<(Polygon, Renderable)>();

		public override void Draw(IEnumerable<Renderable> renderables, Camera camera)
		{
			if (renderables.Count() == 0)
			{
				return;
			}

			base.Draw(renderables, camera);

			VisiblePolygons.Clear();
			foreach (Renderable r in renderables)
			{
				foreach (Polygon p in camera.VisiblePolygons(r))
				{
					VisiblePolygons.Add((p, r));
				}
			}

			GL.ActiveTexture(TextureUnit.Texture0);

			IEnumerable<IGrouping<Texture, (Polygon, Renderable)>> byTexture = VisiblePolygons
				.GroupBy(pair => pair.Item1.Texture)
				.OrderBy(t => t.Key.Translucent);

			foreach (IGrouping<Texture, (Polygon, Renderable)> group in byTexture)
			{
				SetUniform(LocationTextureWidth, (float)group.Key.Width);
				SetUniform(LocationTextureHeight, (float)group.Key.Height);

				GL.BindTexture(TextureTarget.Texture2D, BackEnd.Textures[group.Key.Name.ToLower()]);

				foreach ((Polygon p, Renderable r) in group)
				{
					SetUniform(LocationModelMatrix, r.ModelMatrix);

					SetUniform(LocationBasisS, p.BasisS);
					SetUniform(LocationBasisT, p.BasisT);
					SetUniform(LocationOffset, p.Offset);
					SetUniform(LocationScale, p.Scale);

					GL.DrawElements(PrimitiveType.Triangles, p.Indices.Count, DrawElementsType.UnsignedInt, p.IndexOffset);
				}
			}
		}
	}
}
