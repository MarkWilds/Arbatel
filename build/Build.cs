using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.MSBuild;
using Nuke.Common.Tools.Nunit;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Writers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.MSBuild.MSBuildTasks;
using static Nuke.Common.Tools.NuGet.NuGetTasks;
using static Nuke.Common.Tools.Nunit.NunitTasks;

class Build : NukeBuild
{
	const string ProductName = "Arbatel";

	AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
	AbsolutePath SourceDirectory => RootDirectory / "src";
	AbsolutePath StagingDirectory => RootDirectory / "staging";
	AbsolutePath TestSourceDirectory => RootDirectory / "test" / "src";

	[Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
	readonly string Configuration = IsLocalBuild ? "Debug" : "Release";

	[Solution(ProductName + ".sln")]
	readonly Solution Solution;

	[GitRepository]
	readonly GitRepository GitRepository;

	[GitVersion]
	readonly GitVersion GitVersion;

	public static Dictionary<PlatformFamily, string> OsFriendlyName = new Dictionary<PlatformFamily, string>
	{
		{ PlatformFamily.Unknown, "" },
		{ PlatformFamily.Windows, "Windows" },
		{ PlatformFamily.Linux, "Linux" },
		{ PlatformFamily.OSX, "macOS" },
	};

	/// <summary>
	/// Find the full path of a copy of MSBuild, as installed by Visual Studio
	/// 2017 on a Windows system. Assumes VS2017 Update 2 or newer is installed.
	/// </summary>
	/// <returns>The full path to MSBuild.exe</returns>
	public static string GetMsBuildPath()
	{
		// Visual Studio 2017 Update 2 and newer install vswhere by default.
		return GetMsBuildPath("vswhere.exe");
	}
	/// <summary>
	/// Find the full path of a copy of MSBuild, as installed by Visual Studio
	/// 2017 on a Windows system.
	/// </summary>
	/// <param name="vswherePath">The path to a copy of Microsoft's "vswhere" utility.</param>
	/// <returns>The full path to MSBuild.exe</returns>
	public static string GetMsBuildPath(string vswherePath)
	{
		if (!EnvironmentInfo.IsWin)
		{
			throw new PlatformNotSupportedException("GetMsBuildPath only works in Windows!");
		}

		string args = "-latest -products * -requires Microsoft.Component.MSBuild";

		var vswhere = new Process();
		vswhere.StartInfo.FileName = vswherePath;
		vswhere.StartInfo.UseShellExecute = false;
		vswhere.StartInfo.RedirectStandardOutput = true;
		vswhere.StartInfo.CreateNoWindow = true;

		vswhere.StartInfo.Arguments = String.Join(' ', args, "-property installationPath");
		vswhere.Start();
		var output = new StringBuilder();
		while (!vswhere.StandardOutput.EndOfStream)
		{
			output.Append(vswhere.StandardOutput.ReadLine());
		}
		string vsPath = output.ToString();

		vswhere.StartInfo.Arguments = String.Join(' ', args, "-property installationVersion");
		vswhere.Start();
		output.Clear();
		while (!vswhere.StandardOutput.EndOfStream)
		{
			output.Append(vswhere.StandardOutput.ReadLine());
		}
		Int32.TryParse(output.ToString().Split('.')[0], out int vsMajor);

		if (vsMajor < 15)
		{
			throw new Exception("Can't build with less than VS 2017!");
		}

		return Path.Combine(vsPath, "MSBuild", vsMajor.ToString() + ".0", "Bin", "MSBuild.exe");
	}

	public static int Main()
	{
		if (EnvironmentInfo.IsOsx)
		{
			return Execute<Build>(x => x.PackageMac);
		}
		else if (EnvironmentInfo.IsLinux)
		{
			return Execute<Build>(x => x.PackageLinux);
		}
		else
		{
			return Execute<Build>(x => x.PackageWindows);
		}
	}

	Target Clean => _ => _
		.Executes(() =>
		{
			// Using SetTargets("Rebuild") in the compile targets below would
			// ensure that everything got cleaned and built fresh every time the
			// build scripts were run. Unfortunately, project references in
			// .csproj files mean that dependencies would also be rebuilt, which
			// in this case means the core class library. To start fresh every
			// time, but still build the core library only once, it's important
			// to clean all the bin and obj folders along with the staging
			// directory. Then use SetTargets("Build") on each GUI project to
			// trigger an incremental build. The core will have been built anew,
			// and no time will be wasted rebuilding it for every GUI app.

			var files = new List<string>();
			files.AddRange(GlobFiles(SourceDirectory, "**/bin/**/*", "**/obj/**/*"));
			files.AddRange(GlobFiles(TestSourceDirectory, "**/bin/**/*", "**/obj/**/*"));

			// Make sure all files are gone first; can't remove empty folders.
			foreach (string file in files)
			{
				DeleteFile(file);
			}

			var directories = new List<string>();
			directories.AddRange(GlobDirectories(SourceDirectory, "**/bin/**", "**/obj/**"));
			directories.AddRange(GlobDirectories(TestSourceDirectory, "**/bin/**", "**/obj/**"));

			// Sort paths by length, and reverse to delete the deepest first;
			// the parent levels will then be empty and cleanly deletable.
			var list = new List<string>(directories);
			list.Sort((a, b) => a.Length.CompareTo(b.Length));
			list.Reverse();
			DeleteDirectories(list);

			EnsureCleanDirectory(StagingDirectory);
		});

	Target CompileCore => _ => _
		.DependsOn(Clean)
		.Executes(() =>
		{
			AbsolutePath projectDir = RootDirectory / "src" / $"{ProductName}.Core";
			AbsolutePath project = projectDir / $"{ProductName}.Core.csproj";

			MSBuildSettings settings = new MSBuildSettings()
				.SetProjectFile(project)
				.SetConfiguration(Configuration);

			// Windows developers with Visual Studio installed to a directory
			// other than System.Environment.SpecialFolder.ProgramFilesX86 need
			// to tell Nuke the path to MSBuild.exe themselves.
			if (EnvironmentInfo.IsWin)
			{
				settings = settings.SetToolPath(GetMsBuildPath());
			}

			MSBuildProject parsed = MSBuildParseProject(project, s => settings);

			AbsolutePath buildDir = projectDir / parsed.Properties["OutputPath"];

			MSBuild(s => settings
				.EnableRestore()
				.SetTargets("Build")
				.SetAssemblyVersion(GitVersion.GetNormalizedAssemblyVersion())
				.SetFileVersion(GitVersion.GetNormalizedFileVersion())
				.SetInformationalVersion(GitVersion.InformationalVersion)
				.SetMaxCpuCount(Environment.ProcessorCount)
				.SetNodeReuse(IsLocalBuild));
		});

	Target CompileWindows => _ => _
		.DependsOn(CompileCore)
		.Executes(() =>
		{
			foreach (string etoPlatform in new string[] { "WinForms", "Wpf" })
			{
				AbsolutePath projectDir = RootDirectory / "src" / "gui" / $"{ProductName}.{etoPlatform}";
				AbsolutePath project = projectDir / $"{ProductName}.{etoPlatform}.csproj";

				MSBuildSettings settings = new MSBuildSettings()
					.SetProjectFile(project)
					.SetConfiguration(Configuration);

				if (EnvironmentInfo.IsWin)
				{
					settings = settings.SetToolPath(GetMsBuildPath());
				}

				MSBuildProject parsed = MSBuildParseProject(project, s => settings);

				AbsolutePath buildDir = projectDir / parsed.Properties["OutputPath"];

				MSBuild(s => settings
					.EnableRestore()
					.SetTargets("Build")
					.SetAssemblyVersion(GitVersion.GetNormalizedAssemblyVersion())
					.SetFileVersion(GitVersion.GetNormalizedFileVersion())
					.SetInformationalVersion(GitVersion.InformationalVersion)
					.SetMaxCpuCount(Environment.ProcessorCount)
					.SetNodeReuse(IsLocalBuild));

				CopyDirectoryRecursively(buildDir, StagingDirectory / $"{etoPlatform}");
			}
		});

	Target CompileLinux => _ => _
		.DependsOn(CompileCore)
		.Executes(() =>
		{
			string etoPlatform = "Gtk";

			Project project = Solution.GetProject($"{ProductName}.{etoPlatform}");

			MSBuildSettings settings = new MSBuildSettings()
				.SetProjectFile(project)
				.SetConfiguration(Configuration);

			if (EnvironmentInfo.IsWin)
			{
				settings = settings.SetToolPath(GetMsBuildPath());
			}

			MSBuildProject parsed = MSBuildParseProject(project, s => settings);

			AbsolutePath buildDir = project.Directory / parsed.Properties["OutputPath"];

			MSBuild(s => settings
				.EnableRestore()
				.SetTargets("Build")
				.SetAssemblyVersion(GitVersion.GetNormalizedAssemblyVersion())
				.SetFileVersion(GitVersion.GetNormalizedFileVersion())
				.SetInformationalVersion(GitVersion.InformationalVersion)
				.SetMaxCpuCount(Environment.ProcessorCount)
				.SetNodeReuse(IsLocalBuild));

			CopyDirectoryRecursively(buildDir, StagingDirectory / $"{etoPlatform}");
		});

	Target CompileMac => _ => _
		.DependsOn(CompileCore)
		.Executes(() =>
		{
			foreach (string etoPlatform in new string[] { "Mac", "XamMac" })
			{
				AbsolutePath projectDir = RootDirectory / "src" / "gui" / $"{ProductName}.{etoPlatform}";
				AbsolutePath project = projectDir / $"{ProductName}.{etoPlatform}.csproj";

				MSBuildSettings settings = new MSBuildSettings()
					.SetProjectFile(project)
					.SetConfiguration(Configuration);

				if (EnvironmentInfo.IsWin)
				{
					settings = settings.SetToolPath(GetMsBuildPath());
				}

				MSBuildProject parsed = MSBuildParseProject(project, s => settings);

				AbsolutePath buildDir = projectDir / parsed.Properties["OutputPath"];

				MSBuild(s => settings
					.EnableRestore()
					.SetTargets("Build")
					.SetAssemblyVersion(GitVersion.GetNormalizedAssemblyVersion())
					.SetFileVersion(GitVersion.GetNormalizedFileVersion())
					.SetInformationalVersion(GitVersion.InformationalVersion)
					.SetMaxCpuCount(Environment.ProcessorCount)
					.SetNodeReuse(IsLocalBuild));

				CopyDirectoryRecursively(buildDir, StagingDirectory / $"{etoPlatform}");
			}
		});

	Target CompileTests => _ => _
		.DependsOn(CompileCore)
		.Executes(() =>
		{
			Project project = Solution.GetProject($"{ProductName}Test.Core");

			MSBuildSettings settings = new MSBuildSettings()
				.SetProjectFile(project)
				.SetConfiguration(Configuration);

			if (EnvironmentInfo.IsWin)
			{
				settings = settings.SetToolPath(GetMsBuildPath());
			}

			MSBuildProject parsed = MSBuildParseProject(project, s => settings);

			AbsolutePath buildDir = project.Directory / parsed.Properties["OutputPath"];

			MSBuild(s => settings
				.EnableRestore()
				.SetTargets("Build")
				.SetAssemblyVersion(GitVersion.GetNormalizedAssemblyVersion())
				.SetFileVersion(GitVersion.GetNormalizedFileVersion())
				.SetInformationalVersion(GitVersion.InformationalVersion)
				.SetMaxCpuCount(Environment.ProcessorCount)
				.SetNodeReuse(IsLocalBuild));

			CopyDirectoryRecursively(buildDir, StagingDirectory / project.Name);
		});

	private void Test(string executable)
	{
		Project project = Solution.GetProject($"{ProductName}Test.Core");

		Nunit3Settings settings = new Nunit3Settings()
			.SetToolPath(StagingDirectory / $"{project.Name}" / executable)
			.AddParameter("dataDirectory", RootDirectory / "test/data")
			.AddParameter("fgdDirectory", RootDirectory / "extras");

		if (Configuration != "Release")
		{
			settings = settings.SetWhereExpression("cat != Performance");
		}

		Nunit3(s => settings);
	}

	Target TestWindows => _ => _
		.DependsOn(CompileWindows, CompileTests)
		.Executes(() =>
		{
			Test($"{ProductName}Test.Core.exe");
		});

	Target TestLinux => _ => _
		.DependsOn(CompileLinux, CompileTests)
		.Executes(() =>
		{
			Test($"{ProductName}Test.Core.exe");
		});

	Target TestMac => _ => _
		.DependsOn(CompileMac, CompileTests)
		.Executes(() =>
		{
			Test($"{ProductName}Test.Core.app/Contents/MacOS/{ProductName}Test.Core");
		});

	Target PackageWindows => _ => _
		.DependsOn(TestWindows)
		.Executes(() =>
		{
			foreach (string etoPlatform in new string[] { "WinForms", "Wpf" })
			{
				AbsolutePath finalDir = ArtifactsDirectory / etoPlatform;

				EnsureExistingDirectory(finalDir);

				using (var archive = ZipArchive.Create())
				{
					archive.DeflateCompressionLevel = CompressionLevel.BestCompression;
					archive.AddAllFromDirectory(StagingDirectory / etoPlatform);

					string name = String.Join('-', ProductName, GitVersion.MajorMinorPatch, OsFriendlyName[EnvironmentInfo.Platform]);
					archive.SaveTo(finalDir / name + ".zip", new WriterOptions(CompressionType.Deflate));
				}
			}
		});

	Target PackageLinux => _ => _
		.DependsOn(TestLinux)
		.Executes(() =>
		{
			string etoPlatform = "Gtk";

			AbsolutePath finalDir = ArtifactsDirectory / etoPlatform;

			EnsureExistingDirectory(finalDir);

			string name = String.Join('-', ProductName, GitVersion.MajorMinorPatch, OsFriendlyName[EnvironmentInfo.Platform]);

			var tarball = TarArchive.Create();

			tarball.AddAllFromDirectory(StagingDirectory / etoPlatform);
			tarball.SaveTo(finalDir / name + ".tar.gz", new WriterOptions(CompressionType.GZip));
		});

	Target PackageMac => _ => _
		.DependsOn(TestMac)
		.Executes(() =>
		{
			foreach (string etoPlatform in new string[] { "Mac", "XamMac" })
			{
				AbsolutePath finalDir = ArtifactsDirectory / etoPlatform;

				EnsureExistingDirectory(finalDir);

				string name = String.Join('-', ProductName, GitVersion.MajorMinorPatch, OsFriendlyName[EnvironmentInfo.Platform]);

				var dmgbuild = new Process();
				dmgbuild.StartInfo.FileName = "dmgbuild";
				dmgbuild.StartInfo.Arguments =
					$"-s " + BuildProjectDirectory / "dmgbuild-settings.py" +
					" -D app=" + StagingDirectory / etoPlatform / $"{ProductName}.{etoPlatform}.app" +
					$" {ProductName} " +
					finalDir / name + ".dmg";

				dmgbuild.StartInfo.UseShellExecute = false;
				dmgbuild.StartInfo.CreateNoWindow = true;

				dmgbuild.Start();
				dmgbuild.WaitForExit();
			}
		});
}
