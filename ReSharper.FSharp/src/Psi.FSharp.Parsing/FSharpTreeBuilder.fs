﻿namespace JetBrains.ReSharper.Psi.FSharp.Parsing

open System
open JetBrains.ReSharper.Psi
open JetBrains.ReSharper.Psi.ExtensionsAPI.Tree
open JetBrains.ReSharper.Psi.FSharp.Impl.Tree
open JetBrains.ReSharper.Psi.Parsing
open JetBrains.ReSharper.Psi.TreeBuilder
open JetBrains.Util.dataStructures.TypedIntrinsics
open Microsoft.FSharp.Compiler.Ast
open Microsoft.FSharp.Compiler

type FSharpTreeBuilder(file : IPsiSourceFile, lexer : ILexer, ast : ParsedInput, lifetime) as this =
    inherit TreeStructureBuilderBase(lifetime)

    let document = file.Document
    let builder = PsiBuilder(lexer, ElementType.F_SHARP_IMPL_FILE, this, lifetime)

    member private x.GetLineOffset line = document.GetLineStartOffset(line - 1 |> Int32.op_Explicit)
    member private x.GetStartOffset (range : Range.range) = x.GetLineOffset range.StartLine + range.StartColumn
    member private x.GetEndOffset (range : Range.range) = x.GetLineOffset range.EndLine + range.EndColumn
    member private x.GetStartOffset (id : Ident) = x.GetStartOffset id.idRange
    member private x.IsPastEndOfFile = builder.GetTokenType() |> isNull 

    member private x.AdvanceToFileEnd () =
        while not x.IsPastEndOfFile do builder.AdvanceLexer() |> ignore

    member private x.AdvanceToOffset offset =
        while builder.GetTokenOffset() < offset && not x.IsPastEndOfFile do builder.AdvanceLexer() |> ignore


    member x.CreateFSharpFile() =
        let fileMark = builder.Mark()

        let elementType =
            match ast with
            | ParsedInput.ImplFile (ParsedImplFileInput(_,_,_,_,_,modulesAndNamespaces,_)) ->
                List.iter x.ProcessModuleOrNamespaceDeclaration modulesAndNamespaces
                ElementType.F_SHARP_IMPL_FILE
            | ParsedInput.SigFile (ParsedSigFileInput(_,_,_,_,modulesAndNamespacesSignatures)) ->
                List.iter x.ProcessModuleOrNamespaceSignature modulesAndNamespacesSignatures
                ElementType.F_SHARP_SIG_FILE

        x.AdvanceToFileEnd()
        x.Done(fileMark, elementType)
        x.GetTree() :> ICompositeElement


    // Top level modules and namespaces

    member private x.ProcessModuleOrNamespaceDeclaration (SynModuleOrNamespace(lid,_,isModule,decls,_,_,_,range)) =
        x.ProcessModuleOrNamespace(lid, isModule, range, (fun () -> List.iter x.ProcessModuleMemberDeclaration decls))
        
    member private x.ProcessModuleOrNamespaceSignature (SynModuleOrNamespaceSig(lid,_,isModule,sigs,_,_,_,range)) =
        x.ProcessModuleOrNamespace(lid, isModule, range, (fun () -> List.iter x.ProcessModuleMemberSignature sigs))

    member private x.ProcessModuleOrNamespace(lid, isModule, range, processModuleDeclsFun) =
        // When top level namespace or module identifier is missing
        // its ident name is replaced with file name and the range is 1,0-1,0.
        
        // Namespace range starts after its identifier for some reason,
        // try to locate a keyword after which there may be access modifiers
        let keywordTokenType = if isModule then FSharpTokenType.MODULE else FSharpTokenType.NAMESPACE
        x.GetStartOffset lid.Head |> x.AdvanceToKeywordOrOffset keywordTokenType
        let mark = builder.Mark()
        builder.AdvanceLexer() |> ignore // ignore keyword token

        if isModule then x.ProcessAccessModifiers lid.Head
        x.ProcessLongIdentifier lid
        processModuleDeclsFun()

        range |> x.GetEndOffset |> x.AdvanceToOffset
        x.Done(mark, if isModule then ElementType.TOP_LEVEL_MODULE_DECLARATION else ElementType.F_SHARP_NAMESPACE_DECLARATION)


    // Module members

    member private x.ProcessModuleMemberDeclaration moduleMember =
        match moduleMember with
        | SynModuleDecl.NestedModule(ComponentInfo(_,_,_,lid,_,_,_,_),_,decls,_,range) as decl ->
            range |> x.GetStartOffset |> x.AdvanceToOffset
            let mark = builder.Mark()
            builder.AdvanceLexer() |> ignore // ignore keyword token

            let id = List.head lid 
            x.ProcessAccessModifiers id
            x.ProcessIdentifier id // always single identifier or not parsed at all instead

            List.iter x.ProcessModuleMemberDeclaration decls

            decl.Range |> x.GetEndOffset |> x.AdvanceToOffset
            x.Done(mark, ElementType.NESTED_MODULE_DECLARATION)
        | SynModuleDecl.Types(types,_) -> List.iter x.ProcessType types
        | SynModuleDecl.Exception(exceptionDefn,_) -> x.ProcessException exceptionDefn
        | SynModuleDecl.Open(lidWithDots,range) ->
            range |> x.GetStartOffset |> x.AdvanceToOffset
            let openMark = builder.Mark()
            x.ProcessLongIdentifier lidWithDots.Lid
            range |> x.GetEndOffset |> x.AdvanceToOffset
            x.Done(openMark, ElementType.OPEN)
        | decl ->
            decl.Range |> x.GetStartOffset |> x.AdvanceToOffset
            let declMark = builder.Mark()
            decl.Range |> x.GetEndOffset |> x.AdvanceToOffset
            x.Done(declMark, ElementType.OTHER_MEMBER_DECLARATION)

    member private x.ProcessModuleMemberSignature moduleMember =
        match moduleMember with
        | SynModuleSigDecl.NestedModule(ComponentInfo(_,_,_,lid,_,_,_,_),_,sigs,range) as decl ->
            range |> x.GetStartOffset |> x.AdvanceToOffset
            let mark = builder.Mark()
            builder.AdvanceLexer() |> ignore // ignore keyword token

            let id = List.head lid 
            x.ProcessAccessModifiers id
            x.ProcessIdentifier id // always single identifier or not parsed at all instead

            List.iter x.ProcessModuleMemberSignature sigs

            decl.Range |> x.GetEndOffset |> x.AdvanceToOffset
            x.Done(mark, ElementType.NESTED_MODULE_DECLARATION)
        | _ -> ()
        ()

    member private x.ProcessIdentifier (id : Ident) =
        let range = id.idRange
        x.AdvanceToOffset (x.GetStartOffset range)
        let mark = builder.Mark()
        x.AdvanceToOffset (x.GetEndOffset range)
        x.Done(mark, ElementType.F_SHARP_IDENTIFIER)
    
    /// Should be called on access modifiers start offset.
    /// Modifiers always go right before an identifier or type parameters.
    member private x.ProcessAccessModifiers (id : Ident) =
        x.ProcessAccessModifiers (x.GetStartOffset id)
    
    /// Should be called on access modifiers start offset.
    /// Modifiers always go right before an identifier or type parameters.
    member private x.ProcessAccessModifiers (endOffset : int) =
        let accessModifiersMark = builder.Mark()
        x.AdvanceToOffset endOffset
        builder.Done(accessModifiersMark, ElementType.ACCESS_MODIFIERS, null)

    member private x.ProcessTypeParameterOfType p =
        x.ProcessTypeParameter ElementType.TYPE_PARAMETER_OF_TYPE_DECLARATION p

    member private x.ProcessTypeParameterOfMethod p =
        x.ProcessTypeParameter ElementType.TYPE_PARAMETER_OF_METHOD_DECLARATION p

    member private x.ProcessTypeParameter elementType (TyparDecl(_,(Typar(id,_,_)))) =
        id |> x.GetStartOffset |> x.AdvanceToOffset
        let mark = builder.Mark()
        x.ProcessIdentifier id
        x.Done(mark, elementType)
        

    member private x.ProcessException (SynExceptionDefn(SynExceptionDefnRepr(_,(UnionCase(_,id,_,_,_,_)),_,_,_,_),_,range)) =
        range |> x.GetStartOffset |> x.AdvanceToOffset
        let mark = builder.Mark()
        builder.AdvanceLexer() |> ignore // ignore keyword token

        x.ProcessAccessModifiers id
        x.ProcessIdentifier id

        range |> x.GetEndOffset |> x.AdvanceToOffset
        builder.Done(mark, ElementType.F_SHARP_EXCEPTION_DECLARATION, null)

    member private x.IsTypedCase (UnionCase(_,_,fieldType,_,_,_)) =
        match fieldType with
        | UnionCaseFields([]) -> false
        | _ -> true

    member private x.ProcessUnionCase (UnionCase(_,id,caseType,_,_,range) as case) =
        if x.IsTypedCase case then
            range |> x.GetStartOffset |> x.AdvanceToOffset
            let exnMark = builder.Mark()
            x.ProcessIdentifier id

    //        processUnionCaseTypes caseType

            range |> x.GetEndOffset |> x.AdvanceToOffset
    //        let elementType = if isTypedCase case
    //                          then ElementType.F_SHARP_TYPED_UNION_CASE_DECLARATION
    //                          else ElementType.F_SHARP_SINGLETON_UNION_CASE_DECLARATION
            builder.Done(exnMark, ElementType.F_SHARP_TYPED_UNION_CASE_DECLARATION, null)

    member private x.AdvanceToKeywordOrOffset (keyword : TokenNodeType) (maxOffset : int) =
        while builder.GetTokenOffset() < maxOffset &&
              (not (builder.GetTokenType().IsKeyword) || builder.GetTokenType() <> keyword) do
            builder.AdvanceLexer() |> ignore

    member private x.ProcessLongIdentifier (lid : Ident list) =
        let startOffset = x.GetStartOffset (List.head lid).idRange
        let endOffset = x.GetEndOffset (List.last lid).idRange

        x.AdvanceToOffset startOffset
        let mark = builder.Mark()
        x.AdvanceToOffset endOffset
        x.Done(mark, ElementType.LONG_IDENTIFIER)

    member private x.ProcessAttribute (attr : SynAttribute) =
        x.AdvanceToOffset (x.GetStartOffset attr.Range)
        let mark = builder.Mark()
        x.ProcessLongIdentifier attr.TypeName.Lid
        x.AdvanceToOffset (x.GetEndOffset attr.Range)
        x.Done(mark, ElementType.F_SHARP_ATTRIBUTE)

    member private x.ProcessEnumCase (EnumCase(_,id,_,_,range)) =
        range |> x.GetStartOffset |> x.AdvanceToOffset
        let mark = builder.Mark()
        x.ProcessIdentifier id

        range |> x.GetEndOffset |> x.AdvanceToOffset
        x.Done(mark, ElementType.F_SHARP_ENUM_MEMBER_DECLARATION)

    member private x.ProcessField (Field(_,_,id,_,_,_,_,range)) =
        let mark =
            match id with
            | Some id ->
                let startOffset = min (x.GetStartOffset id) (x.GetStartOffset range)
                x.AdvanceToOffset startOffset
                let mark = builder.Mark()
                x.ProcessIdentifier id
                mark
            | None ->
                range |> x.GetStartOffset |> x.AdvanceToOffset
                builder.Mark()

        range |> x.GetEndOffset |> x.AdvanceToOffset
        x.Done(mark, ElementType.F_SHARP_FIELD_DECLARATION)

    member private x.ProcessLocalDeclaration id (range : Range.range)=
        range |> x.GetStartOffset |> x.AdvanceToOffset
        let mark = x.Mark()
        x.ProcessIdentifier id
        range |> x.GetEndOffset |> x.AdvanceToOffset
        x.Done(mark, ElementType.LOCAL_DECLARATION)

    member private x.ProcessLocalDeclarationWithoutId (range : Range.range)=
        range |> x.GetStartOffset |> x.AdvanceToOffset
        let mark = x.Mark()
        range |> x.GetEndOffset |> x.AdvanceToOffset
        x.Done(mark, ElementType.LOCAL_DECLARATION)

    member private x.ProcessSimplePattern (pat : SynSimplePat) =
        match pat with
        | SynSimplePat.Id(id,_,_,_,_,range) ->
            x.ProcessLocalDeclaration id range
        | SynSimplePat.Typed(SynSimplePat.Id(id,_,_,_,_,range),_,_) ->
            x.ProcessLocalDeclaration id range
        | _ -> ()

    member private x.ProcessImplicitCtor (args : SynSimplePat list) (selfId : Ident option) =
        List.iter x.ProcessSimplePattern args
        match selfId with
        | Some id ->
            let idRange = id.idRange
            idRange |> x.GetStartOffset |> x.AdvanceToOffset
            let selfIdMark = x.Mark()
            idRange |> x.GetEndOffset |> x.AdvanceToOffset
            x.Done(selfIdMark, ElementType.SELF_IDENTIFIER_DECLARATION)
        | _ -> ()

    member private x.ProcessMemberParams memberParams =
        let paramsRanges = 
            match memberParams with
            | Pats(pats) -> List.map (fun (x : SynPat) -> x.Range) pats
            | NamePatPairs(idsAndPats,_) -> List.map (fun (_,pat : SynPat) -> pat.Range) idsAndPats
        List.iter x.ProcessLocalDeclarationWithoutId paramsRanges

    member private x.ProcessTypeMember (typeMember : SynMemberDefn) =
        x.AdvanceToOffset (x.GetStartOffset typeMember.Range)
        let mark = builder.Mark()

        let memberType =
            match typeMember with
            | SynMemberDefn.ImplicitCtor(_,_,args,selfId,_) ->
                x.ProcessImplicitCtor args selfId
                ElementType.IMPLICIT_CONSTRUCTOR_DECLARATION
            | SynMemberDefn.ImplicitInherit(SynType.LongIdent(lidWithDots),_,_,_) ->
                x.ProcessLongIdentifier lidWithDots.Lid
                ElementType.TYPE_INHERIT
            | SynMemberDefn.Interface(SynType.LongIdent(lidWithDots),_,_) ->
                x.ProcessLongIdentifier lidWithDots.Lid
                ElementType.INTERFACE_IMPLEMENTATION
            | SynMemberDefn.Inherit(SynType.LongIdent(lidWithDots),_,_) ->
                x.ProcessLongIdentifier lidWithDots.Lid
                ElementType.INTERFACE_INHERIT
            | SynMemberDefn.Member(Binding(_,_,_,_,_,_,_,headPat,_,_,_,_),_) ->
                match headPat with
                | SynPat.LongIdent(LongIdentWithDots(lid,_),_,typeParamsOption,memberParams,_,_) ->
                    match lid with
                    | [id] when id.idText = "new" ->
                        x.ProcessMemberParams memberParams
                        ElementType.CONSTRUCTOR_DECLARATION
                    | [id] | _ :: id :: _ ->
                        x.ProcessIdentifier id
                        match typeParamsOption with
                        | Some (SynValTyparDecls(typeParams,_,_)) ->
                            List.iter x.ProcessTypeParameterOfMethod typeParams
                            ()
                        | _ -> ()
                        x.ProcessMemberParams memberParams
                        ElementType.MEMBER_DECLARATION
                    | _ -> ElementType.OTHER_TYPE_MEMBER
                | _ -> ElementType.OTHER_TYPE_MEMBER

//            | SynMemberDefn.LetBindings(_) -> ElementType.LET_BINDINGS
//            | SynMemberDefn.AbstractSlot(_) -> ElementType.ABSTRACT_SLOT
//            | SynMemberDefn.ValField(_) -> ElementType.VAL_FIELD
//            | SynMemberDefn.AutoProperty(_) -> ElementType.AUTO_PROPERTY
//            | SynMemberDefn.Open(_) -> ElementType.OPEN
            | _ -> ElementType.OTHER_TYPE_MEMBER

        x.AdvanceToOffset (x.GetEndOffset typeMember.Range)
        x.Done(mark, memberType)

    member private x.ProcessType (TypeDefn(ComponentInfo(attributes, typeParams,_,lid,_,_,_,_), repr, members, range)) =
        let startOffset = x.GetStartOffset (if List.isEmpty attributes then range else (List.head attributes).TypeName.Range)
        x.AdvanceToOffset startOffset
        let mark = builder.Mark()
        List.iter x.ProcessAttribute attributes
        
        let id = lid.Head
        let idOffset = x.GetStartOffset id

        let typeParamsOffset =
            match List.tryHead typeParams with
            | Some (TyparDecl(_,(Typar(id,_,_)))) -> x.GetStartOffset id
            | None -> idOffset

        x.ProcessAccessModifiers (min idOffset typeParamsOffset)

        if idOffset < typeParamsOffset then
            x.ProcessIdentifier id
            List.iter x.ProcessTypeParameterOfType typeParams
        else
            List.iter x.ProcessTypeParameterOfType typeParams 
            x.ProcessIdentifier id

        let elementType =
            match repr with
            | SynTypeDefnRepr.Simple(simpleRepr, _) ->
                match simpleRepr with
                | SynTypeDefnSimpleRepr.Record(_,fields,_) ->
                    List.iter x.ProcessField fields
                    ElementType.F_SHARP_RECORD_DECLARATION
                | SynTypeDefnSimpleRepr.Enum(enumCases,_) ->
                    List.iter x.ProcessEnumCase enumCases
                    ElementType.F_SHARP_ENUM_DECLARATION
                | SynTypeDefnSimpleRepr.Union(_,cases,_) ->
                    List.iter x.ProcessUnionCase cases
                    ElementType.F_SHARP_UNION_DECLARATION
                | SynTypeDefnSimpleRepr.TypeAbbrev(_) ->
                    ElementType.F_SHARP_TYPE_ABBREVIATION_DECLARATION
                | SynTypeDefnSimpleRepr.None(_) ->
                    ElementType.F_SHARP_ABSTRACT_TYPE_DECLARATION
                | _ -> ElementType.F_SHARP_OTHER_SIMPLE_TYPE_DECLARATION
            | SynTypeDefnRepr.Exception(_) ->
                ElementType.F_SHARP_EXCEPTION_DECLARATION
            | SynTypeDefnRepr.ObjectModel(kind, members, _) ->
                List.iter x.ProcessTypeMember members
                match kind with
                | TyconClass -> ElementType.F_SHARP_CLASS_DECLARATION
                | TyconInterface -> ElementType.F_SHARP_INTERFACE_DECLARATION
                | TyconStruct -> ElementType.F_SHARP_STRUCT_DECLARATION
                | _ -> ElementType.F_SHARP_UNSPECIFIED_OBJECT_TYPE_DECLARATION
        List.iter x.ProcessTypeMember members

        range |> x.GetEndOffset |> x.AdvanceToOffset
        builder.Done(mark, elementType , null)

    override x.Builder = builder
    override x.NewLine = FSharpTokenType.NEW_LINE
    override x.CommentsOrWhiteSpacesTokens = FSharpTokenType.CommentsOrWhitespaces
    override x.GetExpectedMessage(name) = NotImplementedException() |> raise
    
    interface IPsiBuilderTokenFactory with
        member x.CreateToken(tokenType, buffer, startOffset, endOffset) =
            tokenType.Create(buffer, TreeOffset(startOffset), TreeOffset(endOffset))

    