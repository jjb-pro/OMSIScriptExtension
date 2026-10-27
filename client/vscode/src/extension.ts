import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind,
} from "vscode-languageclient/node";

const serverName = process.platform === "win32" ? "omsi-script-language-server.exe" : "omsi-script-language-server";

interface ScriptContextInfo {
    owner: string;
    kind: string;
    scripts: string[];
    position: number;
}

interface ScriptContextResult {
    owners: ScriptContextInfo[];
    gameRoot?: string;
}

let client: LanguageClient | undefined;
let projectSync: vscode.Disposable | undefined;
let output: vscode.OutputChannel;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
    output = vscode.window.createOutputChannel("OMSI Script");
    context.subscriptions.push(output);

    context.subscriptions.push(
        vscode.commands.registerCommand("omsiScript.restartServer", async () => {
            await stopClient();
            await startClient(context);
        }),
        vscode.commands.registerCommand("omsiScript.showScriptContext", showScriptContext),
        vscode.workspace.onDidChangeConfiguration(async (event) => {
            if (event.affectsConfiguration("omsiScript.server.path")) {
                await stopClient();
                await startClient(context);
            }
        }),
    );

    await startClient(context);
}

export async function deactivate(): Promise<void> {
    await stopClient();
}

function findServer(context: vscode.ExtensionContext): string | undefined {
    const configured = vscode.workspace.getConfiguration("omsiScript").get<string>("server.path");
    if (configured) {
        return fs.existsSync(configured) ? configured : undefined;
    }

    const candidates = [
        context.asAbsolutePath(path.join("server", serverName)),
        context.asAbsolutePath(path.join("..", "..", "artifacts", "server", `${platformId()}`, serverName)),
    ];

    const found = candidates.find((candidate) => fs.existsSync(candidate));
    if (found && process.platform !== "win32") {
        try {
            fs.accessSync(found, fs.constants.X_OK);
        } catch {
            fs.chmodSync(found, 0o755);
        }
    }

    return found;
}

function platformId(): string {
    const os = process.platform === "win32" ? "win" : process.platform === "darwin" ? "osx" : "linux";
    return `${os}-${process.arch}`;
}

function settings() {
    const configuration = vscode.workspace.getConfiguration("omsiScript");
    return {
        gamePath: configuration.get<string>("gamePath") || undefined,
        diagnostics: { severity: configuration.get<Record<string, string>>("diagnostics.severity") ?? {} },
        inlayHints: { constants: configuration.get<boolean>("inlayHints.constants") ?? true },
    };
}

async function startClient(context: vscode.ExtensionContext): Promise<void> {
    const server = findServer(context);
    if (!server) {
        const message = `The OMSI Script language server for ${platformId()} was not found. Set "omsiScript.server.path" or install the extension package for your platform.`;
        output.appendLine(message);
        void vscode.window.showErrorMessage(message);
        return;
    }

    output.appendLine(`Starting ${server}`);
    const serverOptions: ServerOptions = {
        run: { command: server, transport: TransportKind.stdio },
        debug: { command: server, transport: TransportKind.stdio },
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [
            { scheme: "file", language: "osc" },
            { scheme: "untitled", language: "osc" },
        ],
        initializationOptions: settings(),
        outputChannel: output,
        synchronize: {
            configurationSection: "omsiScript",
            fileEvents: [
                vscode.workspace.createFileSystemWatcher("**/*.{osc,bus,ovh,sco,hum}"),
                vscode.workspace.createFileSystemWatcher("**/*.txt"),
            ],
        },
    };

    client = new LanguageClient("omsiScript", "OMSI Script Language Server", serverOptions, clientOptions);
    try {
        await client.start();
        projectSync = syncProjectFiles(client);
    } catch (error) {
        output.appendLine(`Failed to start the language server: ${String(error)}`);
        void vscode.window.showErrorMessage("The OMSI Script language server could not be started. See the 'OMSI Script' output for details.");
        client = undefined;
    }
}

const projectFilePattern = /\.(txt|bus|ovh|sco|hum)$/i;

function isProjectFile(document: vscode.TextDocument): boolean {
    return document.uri.scheme === "file" && projectFilePattern.test(document.uri.fsPath);
}

function syncProjectFiles(running: LanguageClient): vscode.Disposable {
    const open = (document: vscode.TextDocument) => {
        if (isProjectFile(document)) {
            void running.sendNotification("textDocument/didOpen", {
                textDocument: { uri: document.uri.toString(), languageId: document.languageId, version: document.version, text: document.getText() },
            });
        }
    };

    vscode.workspace.textDocuments.forEach(open);
    return vscode.Disposable.from(
        vscode.workspace.onDidOpenTextDocument(open),
        vscode.workspace.onDidChangeTextDocument((event) => {
            if (isProjectFile(event.document) && event.contentChanges.length > 0) {
                void running.sendNotification("textDocument/didChange", {
                    textDocument: { uri: event.document.uri.toString(), version: event.document.version },
                    contentChanges: [{ text: event.document.getText() }],
                });
            }
        }),
        vscode.workspace.onDidCloseTextDocument((document) => {
            if (isProjectFile(document)) {
                void running.sendNotification("textDocument/didClose", { textDocument: { uri: document.uri.toString() } });
            }
        }),
    );
}

async function stopClient(): Promise<void> {
    const running = client;
    client = undefined;
    projectSync?.dispose();
    projectSync = undefined;
    if (running) {
        await running.stop();
    }
}

async function showScriptContext(): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!client || !editor || editor.document.languageId !== "osc") {
        return;
    }

    const result = await client.sendRequest<ScriptContextResult>("omsiScript/scriptContext", {
        textDocument: { uri: editor.document.uri.toString() },
        position: { line: 0, character: 0 },
    });

    if (result.owners.length === 0) {
        void vscode.window.showInformationMessage(
            "No .bus, .ovh, .sco or .hum file in the workspace references this script. Open the folder that contains the object file to enable project-wide checks.",
        );
        return;
    }

    const picked = await vscode.window.showQuickPick(
        result.owners.map((owner) => ({
            label: path.basename(owner.owner),
            description: `${owner.kind} · script ${owner.position + 1} of ${owner.scripts.length}`,
            detail: vscode.workspace.asRelativePath(owner.owner),
            owner,
        })),
        { placeHolder: "Objects that load this script" },
    );

    if (picked) {
        const document = await vscode.workspace.openTextDocument(vscode.Uri.file(picked.owner.owner));
        await vscode.window.showTextDocument(document);
    }
}
