// include Fake lib
#r @"packages\FAKE\tools\FakeLib.dll"

open Fake
open Fake.AssemblyInfoFile

// Directories
let buildDir  = @".\build\"
let testDir   = @".\test\"
let deployDir = @".\deploy"
let nugetDir = @".nuget\nuget.exe"
// version info


type ProjectInfo = {
    Name : string
    Title : string
    Description : string
    Version: string
    Authors: string list 
}
let info = {
    Name ="DualityOgvVideoPlayerPlugin"
    Title = "Duality OgvVideo player plugin"
    Description = "Ogv video player plugin for Duality Game engine"
    Version =if isLocalBuild then "1.0-Local" else "1.0."+ buildVersion
    Authors =  ["Andrew O'Connor";"Andrea Magnorsky"]
}
// Targets
Target "Clean" (fun _ ->
    CleanDirs [buildDir; testDir;deployDir]
)

Target "SetVersions" (fun _ ->
    CreateCSharpAssemblyInfo "./CorePlugin/Properties/AssemblyInfo.cs"
        [Attribute.Title info.Title
         Attribute.Description info.Description
         Attribute.Guid "00c8792c-39b8-4558-acf9-03013402301a"
         Attribute.Product info.Name
         Attribute.Version info.Version
         Attribute.FileVersion info.Version]
)

let buildMode = getBuildParamOrDefault "buildMode" "Release"
let setParams defaults =
        { defaults with
            Verbosity = Some(Normal)
            Targets = ["Build"]
            Properties =
                [
                    "Optimize", "True"                    
                    "Configuration", buildMode
                    "AllowUnsafeBlocks", "True"
                ]
        }

let restorePackages () =
  !! "./**/packages.config"
        |> Seq.iter (RestorePackage (fun p ->
            { p with Sources = ["https://www.myget.org/F/6416d9912a7c4d46bc983870fb440d25/"]}))

Target "RestorePackages" (fun _ ->
    Rename "./CorePlugin/packages.config" "./CorePlugin/packages.OgvPlayerCorePlugin.config"

    restorePackages()

    Rename "./CorePlugin/packages.OgvPlayerCorePlugin.config" "./CorePlugin/packages.config"
)

Target "RestorePackagesAndroid" (fun _ ->
    Rename "./CorePlugin/packages.config" "./CorePlugin/packages.OgvPlayerCorePlugin.Android.config"

    restorePackages()
    Rename "./CorePlugin/packages.OgvPlayerCorePlugin.Android.config" "./CorePlugin/packages.config"
)

Target "CompileUnsafe" (fun _ ->              
    build setParams "./DualityOgvPlayer.sln"    
    |> DoNothing  
)

Target "CompileUnsafeAndroid" (fun _ ->              
    build setParams "./DualityOgvPlayer.Android.sln"    
    |> DoNothing  
)


Target "NUnitTest" (fun _ ->
    !! (testDir + @"\NUnit.Test.*.dll")
      |> NUnit (fun p ->
                 {p with
                   DisableShadowCopy = true;
                   OutputFile = testDir + @"TestResults.xml"})
)

Target "CreateNuget" (fun _ ->   
    NuGet (fun p -> 
        {p with 
            Authors = info.Authors
            Project = info.Name
            Description = info.Description
            Version = info.Version
            ToolPath = nugetDir     
            Summary = info.Description            
            PublishUrl = getBuildParamOrDefault "nugetUrl" ""
            AccessKey = getBuildParamOrDefault "nugetkey" ""            
            Publish = hasBuildParam "nugetkey"  
        }) 
        "nuget/OgvPlayerCorePlugin.nuspec"
)
Target "AndroidPack" (fun _ ->    
    
    NuGet (fun p -> 
        {p with
            Authors = info.Authors
            Project = info.Name+".Android"
            Version = info.Version
            Description = info.Description                                           
            OutputPath = deployDir    
            ToolPath = nugetDir                    
            Summary = info.Description       
            WorkingDir = @".\nuget"   
            Tags = "video-player ogv"           
            PublishUrl = getBuildParamOrDefault "nugetUrl" ""
            AccessKey = getBuildParamOrDefault "nugetkey" ""            
            Publish = hasBuildParam "nugetkey"  
            }) 
            "nuget/Duality.OgvVideoPlayePluginr.Android.nuspec"
)

// Dependencies
"Clean"
  ==> "SetVersions"
  ==> "RestorePackages"
  ==> "CompileUnsafe"
  ==> "CreateNuget"
  
"Clean"
  ==> "SetVersions"
  ==> "RestorePackagesAndroid"
  ==> "CompileUnsafeAndroid"
  ==> "AndroidPack"
  
// start build
RunTargetOrDefault "CreateNuget"
