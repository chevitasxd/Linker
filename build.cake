#load "build/paths.cake"
#load "build/version.cake"
#load "build/package.cake"
#load "build/urls.cake"

#addin "nuget:?package=Cake.Npm&version=0.17.0"
#addin "nuget:?package=Cake.Curl&version=4.1.0"

#tool "nuget:?package=GitVersion.CommandLine&version=4.0.0-beta0012"
#tool "nuget:?package=OctopusTools&version=6.7.0"
#tool "nuget:?package=curl"

var target = Argument("target", "Build");

Setup<PackageMetadata>(context => {
    return new PackageMetadata(
        outputDirectory : Argument("packageOutPutDirectory", "packages"), 
        name: "Linker"
    );
});

Task("Hello")
    .Does(() =>
{
    Information("Hello");
});

Task("Compile")
    .Does(() =>
{
    DotNetCoreBuild(Paths.SolutionFile.FullPath);
});

Task("Build-Frontend")
    .Does(() => {
        NpmInstall(settings => settings.FromPath(Paths.FrontendDirectory));
    });

Task("Packages-Zip")
    .IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package => {
        CleanDirectory(package.OutputDirectory);
        package.Extension = "zip";
        DotNetCorePublish(Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true,            
            MSBuildSettings = new DotNetCoreMSBuildSettings { NoLogo = true }
        });

        Zip(Paths.PublishDirectory, package.FullPath);
    });

Task("Octo")
.IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package => {
    CleanDirectory(package.OutputDirectory);
        package.Extension = "nupkg";
        DotNetCorePublish(Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true,
            MSBuildSettings = new DotNetCoreMSBuildSettings { NoLogo = true }
        });   
        OctoPack(package.Name, new OctopusPackSettings{
            Format = OctopusPackFormat.NuPkg,
            Version = package.Version,
            BasePath = Paths.PublishDirectory,
            OutFolder = package.OutputDirectory
        });
});

Task("Test")
    .IsDependentOn("Compile")
    .Does(() =>
{
    DotNetCoreTest(Paths.SolutionFile.FullPath);
});

Task("Version")
    .Does<PackageMetadata>((packageMetadata) =>
{
    packageMetadata.Version = ReadVersionFromProjectFile(Context);
    if (string.IsNullOrEmpty(packageMetadata.Version))
    {
        Information("Using GitVersion");
        packageMetadata.Version =  GitVersion().FullSemVer;
    }   
    
    Information($"Calculated {packageMetadata.Version}");
});

Task("Deploy-Kudu")
    ///.IsDependentOn("Packages-Zip")
    .Does<PackageMetadata>((package) => {
        CurlUploadFile(@"C:\Users\sebastian.aguado\source\repos\lnker-ndc\packages\Linker.3.0.0-wip.1+115.zip", Urls.KuduDeployUrl, new CurlSettings {
            Username = "linker-deployer",
            Password = "cake-workshop_ndcoslo2019",
            RequestCommand = "POST",
            Fail = true,
            ArgumentCustomization = (b) => b.Append("-k"),
            ProgressBar = true,
            Verbose = true
        });
    });

Task("Deploy-Octopus")
    .IsDependentOn("Octo")
    .Does<PackageMetadata>((pkg) =>
{
    OctoPush(Urls.OctoServerDeployUrl.ToString(), EnvironmentVariable("OctoApiKey"), pkg.FullPath,
    new OctopusPushSettings {
        EnableServiceMessages = true,
        ReplaceExisting = true
    });
    OctoCreateRelease("Linker-3",
    new CreateReleaseSettings {
        Server = Urls.OctoServerDeployUrl.AbsoluteUri,
        ApiKey = EnvironmentVariable("ActoApiKey"),
        ReleaseNumber = pkg.Version,
        DefaultPackageVersion = pkg.Version,
        DeployTo = "Test",
        IgnoreExisting = true,
        DeploymentProgress = true,
        WaitForDeployment = true
    });
});

RunTarget(target);