using System.Text.Json.Serialization;

namespace OMSIScript.LanguageServer.Protocol;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = false)]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(DidCloseTextDocumentParams))]
[JsonSerializable(typeof(DidSaveTextDocumentParams))]
[JsonSerializable(typeof(TextDocumentPositionParams))]
[JsonSerializable(typeof(ReferenceParams))]
[JsonSerializable(typeof(RenameParams))]
[JsonSerializable(typeof(PrepareRenameResult))]
[JsonSerializable(typeof(Hover))]
[JsonSerializable(typeof(CompletionList))]
[JsonSerializable(typeof(PublishDiagnosticsParams))]
[JsonSerializable(typeof(List<DocumentSymbol>))]
[JsonSerializable(typeof(List<SymbolInformation>))]
[JsonSerializable(typeof(WorkspaceSymbolParams))]
[JsonSerializable(typeof(DocumentSymbolParams))]
[JsonSerializable(typeof(FoldingRangeParams))]
[JsonSerializable(typeof(List<FoldingRange>))]
[JsonSerializable(typeof(List<DocumentHighlight>))]
[JsonSerializable(typeof(DocumentFormattingParams))]
[JsonSerializable(typeof(DocumentRangeFormattingParams))]
[JsonSerializable(typeof(List<TextEdit>))]
[JsonSerializable(typeof(WorkspaceEdit))]
[JsonSerializable(typeof(CodeActionParams))]
[JsonSerializable(typeof(List<CodeAction>))]
[JsonSerializable(typeof(InlayHintParams))]
[JsonSerializable(typeof(List<InlayHint>))]
[JsonSerializable(typeof(List<Location>))]
[JsonSerializable(typeof(DidChangeWatchedFilesParams))]
[JsonSerializable(typeof(DidChangeWorkspaceFoldersParams))]
[JsonSerializable(typeof(DidChangeConfigurationParams))]
[JsonSerializable(typeof(LogMessageParams))]
[JsonSerializable(typeof(ScriptContextResult))]
[JsonSerializable(typeof(ResponseError))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
