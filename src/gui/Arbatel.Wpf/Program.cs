﻿using Arbatel.Controls;
using Arbatel.UI;
using Eto;
using Eto.Forms;
using Eto.Gl;
using Eto.Gl.WPF_WFControl;
using OpenTK;
using System;
using System.Runtime.InteropServices;
using Veldrid;

namespace Arbatel.Wpf
{
	/// <summary>
	/// Allows Arbatel Viewport instances to accept keyboard input for Tab cycling.
	/// </summary>
	public class FocusablePixelLayoutHandler : Eto.Wpf.Forms.PixelLayoutHandler
	{
		public FocusablePixelLayoutHandler()
		{
			Control.Focusable = true;
		}
	}

	public class PuppetWPFWFGLSurfaceHandler : WPFWFGLSurfaceHandler
	{
		public override void AttachEvent(string id)
		{
			switch (id)
			{
				// Prevent the base surface handler class from attaching its own
				// internal event handler to these events; said handler calls
				// MakeCurrent, uses GL.Viewport, and swaps buffers. That's
				// undesirable here, so just attach the appropriate callback.
				case GLSurface.ShownEvent:
					break;
				case GLSurface.GLDrawEvent:
					WinFormsControl.Paint += (sender, e) => Callback.OnDraw(Widget, EventArgs.Empty);
					break;
				case GLSurface.SizeChangedEvent:
					WinFormsControl.SizeChanged += (sender, e) => Callback.OnSizeChanged(Widget, EventArgs.Empty);
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}
	}

	public class WpfVeldridSurfaceHandler : VeldridSurfaceHandler
	{
		protected override void InitializeOtherApi()
		{
			base.InitializeOtherApi();

			var dummy = new WpfVeldridHost();
			dummy.Loaded += (sender, e) =>
			{
				var source = SwapchainSource.CreateWin32(
					dummy.Hwnd, Marshal.GetHINSTANCE(typeof(VeldridSurface).Module));
				Widget.Swapchain = Widget.GraphicsDevice.ResourceFactory.CreateSwapchain(
					new SwapchainDescription(
						source,
						(uint)Widget.Width,
						(uint)Widget.Height,
						PixelFormat.R32_Float,
						false));

				Callback.OnVeldridInitialized(Widget, EventArgs.Empty);
			};
			dummy.WMPaint += (sender, e) => Callback.OnDraw(Widget, e);
			dummy.WMSize += (sender, e) => Callback.OnResize(Widget, e);

			RenderTarget = WpfHelpers.ToEto(dummy);
		}
	}

	public static class Program
	{
		[STAThread]
		public static void Main(string[] args)
		{
			Toolkit opentk = Core.InitOpenTK();

			Platform platform = Platform.Detect;

			platform.Add<PixelLayout.IHandler>(() => new FocusablePixelLayoutHandler());
			if (Core.UseVeldrid)
			{
				platform.Add<GLSurface.IHandler>(() => new PuppetWPFWFGLSurfaceHandler());
			}
			else
			{
				platform.Add<GLSurface.IHandler>(() => new WPFWFGLSurfaceHandler());
			}
			platform.Add<VeldridSurface.IHandler>(() => new WpfVeldridSurfaceHandler());

			var application = new Application(platform);
			application.UnhandledException += Core.UnhandledExceptionHandler;

			using (opentk)
			{
				application.Run(new MainForm());
			}
		}
	}
}
