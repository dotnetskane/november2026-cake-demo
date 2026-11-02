#!/usr/bin/env dotnet
#:sdk Cake.Sdk

Task("Restore")
	.Does(()=>DotNetRestore("src"));

Task("Build")
	.IsDependentOn("Restore")
	.Does(()=>DotNetBuild(
		"src",
		new DotNetBuildSettings
		{
			NoRestore = true
		}
		));

Task("Test")
	.IsDependentOn("Build")
	.Does(()=>DotNetTest(
		"src",
		new DotNetTestSettings
		{
			NoRestore = true,
			NoBuild = true
		}
		));

Task("Package")
	.IsDependentOn("Test")
	.Does(()=>DotNetPack(
		"src",
		new DotNetPackSettings
		{
			NoRestore = true,
			NoBuild = true
		}
		));

Task("Publish")
	.IsDependentOn("Package");

RunTarget(Argument("target", "Test"));
