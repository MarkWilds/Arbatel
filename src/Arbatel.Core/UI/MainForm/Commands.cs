﻿using Arbatel.Controls;
using Arbatel.Formats;
using Arbatel.Formats.Quake;
using Arbatel.Graphics;
using Arbatel.UI.Preferences;
using Eto.Drawing;
using Eto.Forms;
using OpenTK.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Arbatel.UI
{
	public partial class MainForm : Form
	{
		private Command CmdAbout = new Command { MenuText = "About" };

		private Command CmdAutoReload = new Command { MenuText = "Auto reload" };

		private Command CmdClose = new Command
		{
			MenuText = "&Close",
			Shortcut = Application.Instance.CommonModifier | Keys.W
		};

		private Command CmdFullScreen = new Command
		{
			MenuText = "&Full Screen",
			Shortcut = Keys.F11
		};

		private Command CmdOpen = new Command
		{
			MenuText = "&Open...",
			Shortcut = Application.Instance.CommonModifier | Keys.O
		};

		private Command CmdPreferences = new Command
		{
			MenuText = "&Preferences",
			Shortcut = Application.Instance.CommonModifier | Keys.Comma
		};

		private Command CmdQuit = new Command
		{
			MenuText = "&Quit",
			Shortcut = Application.Instance.CommonModifier | Keys.Q
		};

		private Command CmdReload = new Command
		{
			MenuText = "&Reload",
			Shortcut = Application.Instance.CommonModifier | Keys.Shift | Keys.R
		};

		private Command CmdSaveCollapsedAs = new Command
		{
			MenuText = "&Save collapsed as...",
			Shortcut = Application.Instance.CommonModifier | Application.Instance.AlternateModifier | Keys.S
		};

		private Command CmdShowInstancesHidden = new Command
		{
		};
		private Command CmdShowInstancesTinted = new Command
		{
		};
		private Command CmdShowInstancesNormal = new Command
		{
		};

		private bool IsFullscreen { get; set; } = false;
		private Point OldLocation { get; set; }
		private Size OldSize { get; set; }

		public void InitializeCommands()
		{
			var assembly = Assembly.GetAssembly(typeof(MainForm));

			Version version = assembly.GetName().Version;
			int major = version.Major;
			int minor = version.Minor;
			int build = version.Build;
			int revision = version.Revision;

			CmdAbout.Executed += (sender, e) => MessageBox.Show(this,
				"Arbatel " + major + "." + minor + "\n\n" +
				"build " + build + "\n" +
				"revision " + revision);

			CmdAutoReload.Executed += CmdAutoReload_Executed;

			CmdClose.Executed += CmdClose_Executed;

			CmdFullScreen.Executed += CmdFullScreen_Executed;

			CmdOpen.Executed += CmdOpen_Executed;

			CmdPreferences.Executed += CmdPreferences_Executed;

			CmdReload.Executed += (sender, e) => ReloadMap(MapReloader.File);

			CmdSaveCollapsedAs.Executed += CmdSaveCollapsedAs_Executed;

			CmdShowInstancesHidden.Executed += CmdShowInstancesHidden_Executed;
			CmdShowInstancesTinted.Executed += CmdShowInstancesTinted_Executed;
			CmdShowInstancesNormal.Executed += CmdShowInstancesNormal_Executed;

			CmdQuit.Executed += (sender, e) => { Application.Instance.Quit(); };
		}

		private void CmdAutoReload_Executed(object sender, EventArgs e)
		{
			if (MapReloader != null)
			{
				if (cbxAutoReload.Checked && Map != null)
				{
					MapReloader.Enabled = true;
				}
				else
				{
					MapReloader.Enabled = false;
				}
			}
		}

		private void CmdClose_Executed(object sender, EventArgs e)
		{
			CloseMap();
		}

		private void CmdFullScreen_Executed(object sender, EventArgs e)
		{
			if (Content is Viewport viewport)
			{
				if (viewport.Views[viewport.View].Control is View view)
				{
					if (IsFullscreen)
					{
						WindowStyle = WindowStyle.Default;

						Location = OldLocation;
						Size = OldSize;

						IsFullscreen = false;

						view.Invalidate();
					}
					else
					{
						// These need to be set before the window style is
						// changed, or they won't account for the UI chrome.
						OldLocation = Location;
						OldSize = Size;

						WindowStyle = WindowStyle.None;

						Location = (Point)Screen.Bounds.Location;
						Size = (Size)Screen.Bounds.Size;

						IsFullscreen = true;

						view.Invalidate();
					}
				}
			}
		}

		private void CmdOpen_Executed(object sender, EventArgs e)
		{
			var dlgOpenFile = new OpenFileDialog()
			{
				MultiSelect = false,
				Directory = Settings.Local.LastMapDirectory,
				Filters =
				{
					new FileFilter("Quake map", ".map"),
					new FileFilter("All files", ".*")
				},
				CurrentFilterIndex = 0,
				CheckFileExists = true
			};

			DialogResult result = dlgOpenFile.ShowDialog(this);

			if (result == DialogResult.Cancel)
			{
				return;
			}

			CloseMap();

			Settings.Local.LastMapDirectory = new Uri(Path.GetDirectoryName(dlgOpenFile.FileName));

			OpenMap(dlgOpenFile.FileName);
		}

		private void CmdPreferences_Executed(object sender, EventArgs e)
		{
			using (var dialog = new PreferencesDialog(Settings))
			{
				dialog.ShowModal(this);
			}
		}

		private void CmdSaveCollapsedAs_Executed(object sender, EventArgs e)
		{
			var dlgSaveCollapsedAs = new SaveFileDialog()
			{
				Directory = Settings.Local.LastSaveCollapsedAsDirectory,
				Filters =
				{
					new FileFilter("Quake map", ".map"),
					new FileFilter("All files", ".*")
				}
			};

			DialogResult result = dlgSaveCollapsedAs.ShowDialog(this);

			if (result == DialogResult.Cancel)
			{
				return;
			}

			string finalPath = dlgSaveCollapsedAs.FileName;

			// If the selected file exists, use whatever filename was chosen,
			// since by this point users have already confirmed that they want
			// to overwrite that exact file. Otherwise check the extension.
			if (!File.Exists(finalPath))
			{
				if (!finalPath.EndsWith(".map"))
				{
					finalPath += ".map";
				}
			}

			using (var sw = new StreamWriter(finalPath))
			{
				sw.Write(Map.Collapse().ToString());
			}

			Settings.Local.LastSaveCollapsedAsDirectory = new Uri(Path.GetDirectoryName(dlgSaveCollapsedAs.FileName));
			Settings.Local.Save();
		}

		private void CmdShowInstancesHidden_Executed(object sender, EventArgs e)
		{
			TintInstanceObjects(false, false);
		}
		private void CmdShowInstancesTinted_Executed(object sender, EventArgs e)
		{
			TintInstanceObjects(true, true);
		}
		private void CmdShowInstancesNormal_Executed(object sender, EventArgs e)
		{
			TintInstanceObjects(true, false);
		}

		private void TintInstanceObjects(bool visible, bool tinted)
		{
			string[] names = { "func_instance", "func_placeholder", "misc_external_map" };

			IEnumerable<MapObject> instances =
				from mo in Map.MapObjects
				where names.Contains(mo.Definition.ClassName)
				select mo;

			Color4? tint = null;
			if (!visible)
			{
				tint = new Color4(1.0f, 1.0f, 1.0f, 0.0f);
			}

			foreach (MapObject mo in instances)
			{
				if (visible && tinted)
				{
					tint = mo.Definition.Color;
				}

				TintInstanceObject(mo, tint);
			}

			(Control Control, string Name, Action<Control> SetUp) view = Viewport.Views[Viewport.View];
			view.SetUp.Invoke(view.Control);
		}

		private void TintInstanceObject(MapObject mo, Color4? color)
		{
			TintInstanceObject(mo, color, 0);
		}
		private void TintInstanceObject(MapObject mo, Color4? color, int depth)
		{
			foreach (MapObject child in mo.Children)
			{
				TintInstanceObject(child, color, depth + 1);
			}

			Color4? tint = null;
			// Leave the root instance renderable alone, tint everything else.
			if ((depth > 0 || mo.Definition.ClassName != "func_instance") && mo.Definition.ClassName != "misc_external_map")
			{
				tint = color;
			}

			foreach (Renderable r in mo.Renderables)
			{
				r.Tint = tint;
			}
		}
	}
}
