module JetBrains.ReSharper.Plugins.FSharp.Common.Checker.FSharpCoreFix

open System
open System.IO
open JetBrains.Util

let inline combinePaths path1 (path2 : string) = Path.Combine(path1, path2.TrimStart [| '\\'; '/' |])

let inline (</>) path1 path2 = combinePaths path1 path2

let private programFilesX86 =
    if PlatformUtil.IsRunningUnderWindows then
        FileSystemPath.TryParse(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)).FullPath
    else String.Empty

let private tryFindFile dirs file =
      dirs
      |> Seq.map (fun (path : string) ->
          try
             let path =
                if path.StartsWith("\"") && path.EndsWith("\"")
                then path.Substring(1, path.Length - 2)
                else path
             let dir = new DirectoryInfo(path)
             if not dir.Exists then ""
             else
                 let fi = new FileInfo(dir.FullName </> file)
                 if fi.Exists then fi.FullName
                 else ""
          with
          | _ -> "")
      |> Seq.filter ((<>) "")
      |> Seq.tryHead

let fsharpCoreOpt =
    if PlatformUtil.IsRunningOnMono then
      let mscorlibDir = Path.GetDirectoryName typeof<obj>.Assembly.Location
      if List.forall File.Exists (List.map (combinePaths mscorlibDir) ["FSharp.Core.dll"; "FSharp.Core.optdata"; "FSharp.Core.sigdata"]) then
        Some (mscorlibDir </> "FSharp.Core.dll")
      else
        None
    else
      let referenceAssembliesPath =
        programFilesX86 </> @"Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\"
      let fsharpCoreVersions = ["4.4.1.0"; "4.4.0.0"; "4.3.1.0"; "4.3.0.0"]
      tryFindFile (List.map (combinePaths referenceAssembliesPath) fsharpCoreVersions) "FSharp.Core.dll"

let isFSharpCore (s : string) = s.StartsWith "-r:" && s.EndsWith "FSharp.Core.dll"

let ensureCorrectFSharpCore (options: string[]) =
    fsharpCoreOpt
    |> Option.map (fun path ->
                   let fsharpCoreRef = sprintf "-r:%s" path
                   [| yield fsharpCoreRef
                      yield! options |> Array.filter (not << isFSharpCore) |])
    |> Option.defaultValue options