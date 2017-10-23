namespace JetBrains.ReSharper.Plugins.FSharp.Psi.LanguageService.Parsing

open JetBrains.DataFlow
open JetBrains.ReSharper.Psi
open JetBrains.ReSharper.Psi.Parsing
open JetBrains.ReSharper.Plugins.FSharp.Common.Checker
open JetBrains.ReSharper.Plugins.FSharp.Psi.Tree
open JetBrains.ReSharper.Plugins.FSharp.Psi.Parsing
open JetBrains.ReSharper.Psi.Modules
open JetBrains.ReSharper.Psi.Tree
open JetBrains.Util
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.Ast
open Microsoft.FSharp.Compiler.SourceCodeServices

type FSharpParser(sourceFile, checkerService: FSharpCheckerService, logger) =
    member private x.CreateTreeBuilder lexer (parseResults: FSharpParseFileResults option) lifetime options =
        match options, parseResults with
        | Some options, Some results when results.ParseTree.IsSome ->
            match results.ParseTree with
            | Some(ParsedInput.ImplFile(_)) as treeOpt ->
                FSharpImplTreeBuilder(sourceFile, lexer, treeOpt, lifetime, logger) :> FSharpTreeBuilderBase, treeOpt
            | Some(ParsedInput.SigFile(_)) as treeOpt ->
                FSharpSigTreeBuilder(sourceFile, lexer, treeOpt, lifetime, logger) :> FSharpTreeBuilderBase, treeOpt
        | _ ->
            FSharpFakeTreeBuilder(sourceFile, lexer, lifetime, logger, options) :> FSharpTreeBuilderBase, None

    interface IParser with
        member this.ParseFile() =
            let lifetime = Lifetimes.Define().Lifetime
            let options, parseResults = checkerService.ParseFile(sourceFile)
            let tokenBuffer = TokenBuffer(FSharpLexer(sourceFile.Document, checkerService.GetDefines(sourceFile)))
            let lexer = tokenBuffer.CreateLexer()
            let treeBuilder, tree = this.CreateTreeBuilder lexer parseResults lifetime options

            match treeBuilder.CreateFSharpFile() with
            | :? IFSharpFile as fsFile ->
                fsFile.ParseResults <- parseResults
                fsFile.CheckerService <- checkerService
                fsFile.ActualTokenBuffer <- tokenBuffer
                fsFile :> _
            | _ ->
                logger.LogMessage(LoggingLevel.ERROR, "FSharpTreeBuilder returned null")
                null
