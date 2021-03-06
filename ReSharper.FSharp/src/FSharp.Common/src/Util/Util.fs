﻿namespace JetBrains.ReSharper.Plugins.FSharp.Common.Util

[<AutoOpen>]
module CommonUtil =
    open System
    open System.Collections.Generic
    open System.Linq
    open System.Threading
    open JetBrains.Application
    open JetBrains.Application.Progress
    open JetBrains.DocumentModel
    open JetBrains.ProjectModel
    open JetBrains.ProjectModel.ProjectsHost
    open JetBrains.ProjectModel.Properties
    open JetBrains.ProjectModel.Properties.CSharp
    open JetBrains.ProjectModel.Properties.Managed
    open JetBrains.ReSharper.Plugins.FSharp.ProjectModel.ProjectProperties
    open JetBrains.ReSharper.Plugins.FSharp.ProjectModel.ProjectProperties.FSharpProjectPropertiesFactory
    open JetBrains.ReSharper.Psi.ExtensionsAPI.Tree
    open JetBrains.Util
    open JetBrains.Util.dataStructures.TypedIntrinsics
    open Microsoft.FSharp.Compiler
    open Microsoft.FSharp.Compiler.SourceCodeServices

    let private interruptCheckTimeout = 30

    let inline isNotNull x = not (isNull x)
    
    let inline (|NotNull|_|) x =
        if isNull x then None else Some()

    [<Literal>]
    let FsprojExtension = "fsproj"

    let isFSharpProject (guids: Guid seq) (projectFile: FileSystemPath) =
        projectFile.ExtensionNoDot.Equals(FsprojExtension, StringComparison.OrdinalIgnoreCase) ||
        Seq.exists Factory.IsKnownProjectTypeGuid guids

    let (|FSharProjectMark|_|) (mark: IProjectMark) =
        if isFSharpProject [mark.Guid] mark.Location then Some() else None

    let ensureAbsolute (path: FileSystemPath) (projectDirectory: FileSystemPath) =
        let relativePath = path.AsRelative()
        if isNull relativePath then path
        else projectDirectory.Combine(relativePath)

    let concatErrors errors =
        Seq.fold (fun s (e: FSharpErrorInfo) -> s + "\n" + e.Message) "" errors

    [<CompiledName("DecompileOpName")>]
    let decompileOpName name =
        PrettyNaming.DecompileOpName name
        
    [<CompiledName("RunSynchronouslyWithTimeout")>]
    let runSynchronouslyWithTimeout (action: Func<_>) timeout =
        Async.RunSynchronously(async { return action.Invoke() }, timeout)

    type Async<'T> with
        member x.RunAsTask(?interruptChecker) =
            let interruptChecker = defaultArg interruptChecker (Action(fun _ -> InterruptableActivityCookie.CheckAndThrow()))
            let cancellationTokenSource = new CancellationTokenSource()
            let cancellationToken = cancellationTokenSource.Token
            let task = Async.StartAsTask(x, cancellationToken = cancellationToken)

            while not task.IsCompleted do
                let finished = task.Wait(interruptCheckTimeout, cancellationToken)
                if not finished then
                    try interruptChecker.Invoke()
                    with :? ProcessCancelledException ->
                        cancellationTokenSource.Cancel()
                        reraise()
            task.Result

    type IDictionary<'TKey, 'TValue> with
        member x.remove (key: 'TKey) = x.Remove key |> ignore
        member x.add (key: 'TKey, value: 'TValue) = x.Add(key, value) |> ignore
        member x.contains (key: 'TKey) = x.ContainsKey key

    type ISet<'T> with
        member x.remove el = x.Remove el |> ignore
        member x.add el = x.Add el |> ignore

    type FileSystemPath with
        member x.IsImplFile() =
            let ext = x.ExtensionNoDot
            ext = "fs" || ext = "ml"

        member x.IsSigFile() =
            let ext = x.ExtensionNoDot
            ext = "fsi" || ext = "mli"

    type Line = Int32<DocLine>
    type Column = Int32<DocColumn>

    let docLine (x: int)   = Line.op_Explicit(x)
    let docColumn (x: int) = Column.op_Explicit(x)

    type Range.range with
        member x.GetStartLine()   = x.StartLine - 1 |> docLine
        member x.GetEndLine()     = x.EndLine - 1   |> docLine
        member x.GetStartColumn() = x.StartColumn   |> docColumn
        member x.GetEndColumn()   = x.EndColumn     |> docColumn

        member x.ToTextRange(document: IDocument) =
            let startOffset = document.GetLineStartOffset(x.GetStartLine()) + x.StartColumn
            let endOffset = document.GetLineStartOffset(x.GetEndLine()) + x.EndColumn
            TextRange(startOffset, endOffset)
        
        member x.ToDocumentRange(document: IDocument) =
            DocumentRange(document, x.ToTextRange(document))

    type IProject with
        member x.IsFSharp =
            isFSharpProject x.ProjectProperties.ProjectTypeGuids x.ProjectFileLocation
