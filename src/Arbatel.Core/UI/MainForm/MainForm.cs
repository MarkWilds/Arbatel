using Arbatel.Controls;
using Arbatel.Formats;
using Arbatel.Formats.Quake;
using Arbatel.Graphics;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Arbatel.UI
{
	public partial class MainForm
	{
		/// <summary>
		/// The graphics backend used by this application.
		/// </summary>
		public BackEnd BackEnd { get; private set; }

		private Map _map;
		/// <summary>
		/// The Map currently loaded in this form.
		/// </summary>
		public Map Map
		{
			get { return _map; }
			set
			{
				_map = value;

				(Content as Viewport).Map = _map;
			}
		}

		public Settings Settings { get; } = new Settings();

		public FileSystemWatcher Watcher { get; private set; }

		public MainForm()
		{
			InitializeComponent();

			InitializeCommands();

			Viewport viewport;
			if (Core.UseVeldrid)
			{
				viewport = new VeldridViewport { ID = "viewport" };
			}
			else
			{
				viewport = new OpenGLViewport { ID = "viewport" };
			}

			BackEnd = viewport.BackEnd;

			foreach ((Control Control, string Name, Action<View> SetUp) view in viewport.Views.Values)
			{
				if (view.Control is View v)
				{
					Settings.Updatables.Add(v);
				}
			}

			ButtonMenuItem viewMenu = Menu.Items.GetSubmenu("View");
			foreach (KeyValuePair<int, Command> command in viewport.ViewCommands)
			{
				viewMenu.Items.Insert(command.Key, command.Value);
			}
			viewMenu.Items.Insert(viewport.ViewCommands.Count, new SeparatorMenuItem());

			Content = viewport;

			Shown += SetDefaultView;
		}

		private void Watcher_Changed(object sender, FileSystemEventArgs e)
		{
			var viewport = Content as Viewport;
			var v = viewport.Views[viewport.View].Control as View;

			string oldTitle = Title;

			// Controllers' input clock, and Views' graphics clock, are Eto
			// UITimers, and need to be started and stopped from the UI thread.
			Application.Instance.Invoke(() =>
			{
				Title = "Map file changed, reloading...";
				v.Controller.Deactivate();
				v.GraphicsClock.Stop();
			});

			// CloseMap includes a call to Watcher.Dispose, which will deadlock
			// if performed on the Eto UI thread, so these need to be outside of
			// the anonymous Invoke methods.
			CloseMap();
			OpenMap(e.FullPath);

			Application.Instance.Invoke(() =>
			{
				v.GraphicsClock.Start();
				v.Controller.Activate();
				Title = oldTitle;
			});
		}

		private void OpenMap(string fileName)
		{
			using (var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				string ext = Path.GetExtension(fileName);

				if (ext.ToLower() != ".map")
				{
					throw new InvalidDataException("Unrecognized map format!");
				}

				var definitions = new Dictionary<string, DefinitionDictionary>();

				foreach (string path in Settings.Local.DefinitionDictionaryPaths)
				{
					definitions.Add(path, Loader.LoadDefinitionDictionary(path));
				}

				Map = new QuakeMap(stream, definitions.Values.ToList().Stack());
			}

			Settings.Updatables.Add(Map);
			Settings.Save();

			BackEnd.InitTextures(Map.Textures);

			GetAllThisNonsenseReady();

			Watcher = new FileSystemWatcher
			{
				Path = Path.GetDirectoryName(fileName),
				Filter = Path.GetFileName(fileName),
				NotifyFilter = NotifyFilters.LastWrite,
				EnableRaisingEvents = cbxAutoReload.Checked
			};
			Watcher.Changed += Watcher_Changed;
		}
		private void CloseMap()
		{
			if (Map == null)
			{
				return;
			}

			Watcher.EnableRaisingEvents = false;
			Watcher.Changed -= Watcher_Changed;
			Watcher.Dispose();

			IEnumerable<View> views =
				from view in (Content as Viewport).Views
				where view.Value.Control is View
				select view.Value.Control as View;

			BackEnd.DeleteMap(Map, views);

			Settings.Updatables.Remove(Map);

			Map = null;
		}

		private void GetAllThisNonsenseReady()
		{
			var viewport = FindChild("viewport") as Viewport;

			IEnumerable<View> views =
				from view in viewport.Views
				where view.Value.Control is View
				select view.Value.Control as View;

			BackEnd.InitMap(Map, views.Distinct().ToList());

			if (rdoInstanceHidden.Checked)
			{
				rdoInstanceHidden.Command.Execute(null);
			}
			else if (rdoInstanceTinted.Checked)
			{
				rdoInstanceTinted.Command.Execute(null);
			}
			else if (rdoInstanceNormal.Checked)
			{
				rdoInstanceNormal.Command.Execute(null);
			}

			// TODO: Reenable this once I actually understand data binding in
			// Eto! Currently it's just wasting memory every time users close
			// a map and open a new one. Eventually I want a hierarchical view
			// of the level contents, but we'll get there when we get there.
			//var tree = viewport.Views[1].Control as TreeGridView;
			//tree.Columns.Add(new GridColumn() { HeaderText = "Column 1", DataCell = new TextBoxCell(0) });
			//tree.Columns.Add(new GridColumn() { HeaderText = "Column 2", DataCell = new TextBoxCell(1) });
			//tree.Columns.Add(new GridColumn() { HeaderText = "Column 3", DataCell = new TextBoxCell(2) });
			//tree.Columns.Add(new GridColumn() { HeaderText = "Column 4", DataCell = new TextBoxCell(3) });

			//var items = new List<TreeGridItem>
			//{
			//	new TreeGridItem(new object[] { "first", "second", "third" }),
			//	new TreeGridItem(new object[] { "morpb", "kwang", "wump" }),
			//	new TreeGridItem(new object[] { "dlooob", "oorf", "dimples" }),
			//	new TreeGridItem(new object[] { "wort", "hey", "karen" })
			//};

			//var collection = new TreeGridItemCollection(items);

			//tree.DataStore = collection;
		}

		/// <summary>
		/// Set this form's Viewport to display its default View.
		/// </summary>
		/// <remarks>
		/// This needs to happen well after the Viewport class's LoadComplete
		/// and Shown events are raised, as well as after MainForm.LoadComplete,
		/// but should also only happen once. A self-removing handler up here in
		/// the MainForm class does the trick well enough.
		/// </remarks>
		private void SetDefaultView(object sender, EventArgs e)
		{
			((Viewport)Content).View = Viewport.DefaultView;

			Shown -= SetDefaultView;
		}
	}
}
