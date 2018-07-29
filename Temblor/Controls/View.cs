﻿using Eto;
using Eto.Forms;
using Eto.Gl;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Diagnostics;
using Temblor.Controllers;
using Temblor.Formats;
using Temblor.Graphics;

namespace Temblor.Controls
{
	public class View : GLSurface
	{
		// -- System types
		private float _fps;
		public float Fps
		{
			get
			{
				return _fps;
			}

			set
			{
				_fps = value;
				Clock.Interval = 1.0 / value;
			}
		}

		public string[] VertexShaderSource330 =
		{
			"#version 330 core",
			"layout (location = 0) in vec3 position;",
			"layout (location = 1) in vec3 normal;",
			"layout (location = 2) in vec4 color;",
			"",
			"out vec4 vertexColor;",
			"out vec2 TexCoords;",
			"",
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
			"	// left-handed, Z-up world coordinates. The Camera class,",
			"	// in contrast, uses right-handed, Y-up coordinates.",
			"	vec3 zUpLeftHand = vec3(position.x, position.z, -position.y);",
			"   gl_Position = projection * view * vec4(zUpLeftHand, 1.0f);",
			"	vertexColor = color;",
			"",
			"	float coordS = (dot(position, basisS) + (offset.x * scale.x)) / (textureWidth * scale.x);",
			"	float coordT = (dot(position, basisT) + (offset.y * scale.y)) / (textureHeight * scale.y);",
			"",
			"	TexCoords = vec2(coordS, coordT);",
			"}"
		};
		public string[] FragmentShaderSource330 =
		{
			"#version 330 core",
			"",
			"in vec4 vertexColor;",
			"in vec2 TexCoords;",
			"",
			"out vec4 color;",
			"",
			"uniform sampler2D testTexture;",
			"",
			"void main()",
			"{",
			"   //color = vertexColor;",
			"	color = texture(testTexture, TexCoords);",
			"}"
		};

		// -- Eto
		public UITimer Clock = new UITimer();
		public Label Label = new Label();

		// -- OpenTK
		public Color4 ClearColor;
		public PolygonMode PolygonMode;

		// -- Temblor
		public Camera Camera = new Camera();
		public Controller Controller;

		public Map Map;

		public Shader Shader;

		// Explicitly choosing an eight-bit stencil buffer prevents visual artifacts
		// on the Mac platform; the GraphicsMode defaults are apparently insufficient.
		private static GraphicsMode _mode = new GraphicsMode(new ColorFormat(32), 8, 8, 8);

		// -- Constructors
		public View() : this(_mode, 3, 3, GraphicsContextFlags.Default)
		{

		}
		public View(GraphicsMode _mode, int _major, int _minor, GraphicsContextFlags _flags) :
			base(_mode, _major, _minor, _flags)
		{
			Fps = 60.0f;

			Label.Text = "View";
			Label.BackgroundColor = Eto.Drawing.Colors.Black;
			Label.TextColor = Eto.Drawing.Colors.White;

			Clock.Elapsed += Clock_Elapsed;
			GLInitalized += View_GLInitialized;
			MouseMove += View_MouseMove;
		}

		// -- Methods
		public void Clear()
		{
			GL.Viewport(0, 0, Width, Height);

			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
		}

		public void Refresh()
		{
			Clear();

			Camera.AspectRatio = (float)Width / (float)Height;

			Shader.Use();
			Shader.SetMatrix4("view", ref Camera.ViewMatrix);
			Shader.SetMatrix4("projection", ref Camera.ProjectionMatrix);

			foreach (var mapObject in Map.MapObjects)
			{
				mapObject.Draw(Shader, this);
			}

			SwapBuffers();
		}

		// -- Overrides
		protected override void OnDraw(EventArgs e)
		{
			base.OnDraw(e);

			// OnDraw only gets called in certain circumstances, for example
			// when the application window is resized. During such an event,
			// there's no guarantee that the call to Refresh by this class's
			// clock will happen before the call to OnDraw, in which GLSurface
			// clears the viewport, which means the view will flicker with its
			// clear color. Refreshing when the OnDraw event is raised prevents
			// that, and keeps everything smooth.
			Refresh();
		}

		protected override void OnGotFocus(EventArgs e)
		{
			base.OnGotFocus(e);

			Clock.Interval = 1.0 / Fps;
		}
		protected override void OnLostFocus(EventArgs e)
		{
			base.OnLostFocus(e);

			// A perhaps-unnecessary performance thing; would love to allow full framerate
			// for all visible views, so editing objects is visually smooth everywhere at once.
			Clock.Interval = 1.0 / (Fps / 4.0);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			Clock.Start();
		}
		protected override void OnUnLoad(EventArgs e)
		{
			base.OnUnLoad(e);

			Clock.Stop();

			Controller.MouseLook = false;
			Style = "showcursor";
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			Controller.KeyEvent(this, e);
		}
		protected override void OnKeyUp(KeyEventArgs e)
		{
			Controller.KeyEvent(this, e);
		}

		protected override void OnMouseEnter(MouseEventArgs e)
		{
			base.OnMouseEnter(e);

			Focus();
		}

		public double AverageFps = 0.0;

		// -- Event handlers
		private void Clock_Elapsed(object sender, EventArgs e)
		{
			var sw = Stopwatch.StartNew();
			Controller.Move();
			sw.Stop();
			sw.Reset();

			var elapsedMsMove = sw.ElapsedMilliseconds;

			sw.Start();
			Refresh();
			sw.Stop();

			var elapsedMsRefresh = sw.ElapsedMilliseconds;

			if (ParentWindow != null)
			{
				//ParentWindow.Title = MainForm.triangleCount.ToString();
				//ParentWindow.Title = "Tris: "  + MainForm.triangleCount.ToString() + " Move ms: " + elapsedMsMove.ToString() + " Refresh ms: " + elapsedMsRefresh.ToString();
				ParentWindow.Title = "Move ms: " + elapsedMsMove.ToString() + " Refresh ms: " + elapsedMsRefresh.ToString();
			}
		}

		private void View_GLInitialized(object sender, EventArgs e)
		{
			GL.Enable(EnableCap.DepthTest);

			GL.Enable(EnableCap.CullFace);
			GL.FrontFace(FrontFaceDirection.Ccw);
			GL.CullFace(CullFaceMode.Back);

			// GL.ClearColor has two overloads, and if this class' ClearColor field is
			// passed in, the compiler tries to use the one taking a System.Drawing.Color
			// parameter instead of the one taking an OpenTK.Graphics.Color4. Using the
			// float signature therefore avoids an unnecessary System.Drawing reference.
			GL.ClearColor(ClearColor.R, ClearColor.G, ClearColor.B, ClearColor.A);

			Shader.GetGlslVersion(out int major, out int minor);

			if (major >= 3 && minor >= 3)
			{
				Shader = new Shader(VertexShaderSource330, FragmentShaderSource330);
			}
			else
			{
				// TODO: Bring 1.30 shaders over from CSharpGlTest project.
			}

			GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode);

			// TEST. Also remember to switch Camera to use left-handed, Z-up position at some point.
			Camera.Position = new Vector3(256.0f, 1024.0f, 1024.0f);
			Camera.Pitch = -30.0f;
		}

		private void View_MouseMove(object sender, MouseEventArgs e)
		{
			Controller.MouseMove(sender, e);
		}
	}
}
