const assert = require("assert");
const path = require("path");
const vscode = require("vscode");

async function waitFor(probe, description, timeout = 30000) {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
        const value = await probe();
        if (value) {
            return value;
        }
        await new Promise((resolve) => setTimeout(resolve, 250));
    }
    throw new Error(`Timed out waiting for ${description}`);
}

function codeOf(diagnostic) {
    return typeof diagnostic.code === "object" ? String(diagnostic.code.value) : String(diagnostic.code);
}

function positionAfter(document, text) {
    const offset = document.getText().indexOf(text);
    assert.ok(offset >= 0, `'${text}' not found`);
    return document.positionAt(offset + text.length);
}

function hoverText(hovers) {
    return hovers.flatMap((hover) => hover.contents.map((content) => (typeof content === "string" ? content : content.value))).join("\n");
}

const tests = [];
const test = (name, body) => tests.push({ name, body });

let main;
let lib;

test("diagnoses unknown variables in the project context", async () => {
    const diagnostics = await waitFor(() => {
        const current = vscode.languages.getDiagnostics(main.uri);
        return current.some((d) => codeOf(d) === "OSC2001") ? current : undefined;
    }, "OSC2001 diagnostics");

    const unknown = diagnostics.filter((d) => codeOf(d) === "OSC2001");
    assert.strictEqual(unknown.length, 1);
    assert.strictEqual(main.getText(unknown[0].range), "unknown_var");
    assert.strictEqual(unknown[0].severity, vscode.DiagnosticSeverity.Error);
    assert.deepStrictEqual(vscode.languages.getDiagnostics(lib.uri).filter((d) => d.severity === vscode.DiagnosticSeverity.Error), []);
});

test("shows constant values and documentation on hover", async () => {
    const constant = await vscode.commands.executeCommand("vscode.executeHoverProvider", lib.uri, positionAfter(lib, "(C.L.sp"));
    assert.match(hoverText(constant), /Value: `1\.5`/);

    const timegap = await vscode.commands.executeCommand("vscode.executeHoverProvider", lib.uri, positionAfter(lib, "(L.S.Time"));
    assert.match(hoverText(timegap), /The interval in seconds from the last frame/);
});

test("navigates to macro definitions in other script files", async () => {
    const definitions = await vscode.commands.executeCommand("vscode.executeDefinitionProvider", main.uri, positionAfter(main, "(M.L.lib_"));
    const target = definitions[0].targetUri ?? definitions[0].uri;
    assert.strictEqual(path.basename(target.fsPath), "lib.osc");
});

test("completes variables from varlists", async () => {
    const list = await vscode.commands.executeCommand("vscode.executeCompletionItemProvider", main.uri, positionAfter(main, "(L.L."));
    const labels = list.items.map((item) => (typeof item.label === "string" ? item.label : item.label.label));
    assert.ok(labels.includes("speed_var"), "speed_var missing");
    assert.ok(labels.includes("Velocity"), "predefined Velocity missing");
});

test("offers a quick fix that declares the variable", async () => {
    const diagnostic = vscode.languages.getDiagnostics(main.uri).find((d) => codeOf(d) === "OSC2001");
    const actions = await vscode.commands.executeCommand("vscode.executeCodeActionProvider", main.uri, diagnostic.range);
    assert.ok(actions.some((action) => action.title === "Declare 'unknown_var' in vars.txt"), JSON.stringify(actions.map((a) => a.title)));
});

test("clears the diagnostic after declaring the variable", async () => {
    const diagnostic = vscode.languages.getDiagnostics(main.uri).find((d) => codeOf(d) === "OSC2001");
    const actions = await vscode.commands.executeCommand("vscode.executeCodeActionProvider", main.uri, diagnostic.range);
    const declare = actions.find((action) => action.title === "Declare 'unknown_var' in vars.txt");
    assert.ok(await vscode.workspace.applyEdit(declare.edit));
    await waitFor(() => !vscode.languages.getDiagnostics(main.uri).some((d) => codeOf(d) === "OSC2001"), "OSC2001 to disappear");
});

test("lists blocks in the outline", async () => {
    const symbols = await vscode.commands.executeCommand("vscode.executeDocumentSymbolProvider", lib.uri);
    assert.deepStrictEqual(symbols.map((symbol) => symbol.name), ["lib_frame"]);
});

test("formats without moving comments", async () => {
    const document = await vscode.workspace.openTextDocument({ language: "osc", content: "' keep\n{frame}\n1 {if}\n2\n{endif}\n{end}\n" });
    const edits = await vscode.commands.executeCommand("vscode.executeFormatDocumentProvider", document.uri, { tabSize: 4, insertSpaces: false });
    const workspaceEdit = new vscode.WorkspaceEdit();
    workspaceEdit.set(document.uri, edits);
    await vscode.workspace.applyEdit(workspaceEdit);
    assert.strictEqual(document.getText(), "' keep\n{frame}\n\t1 {if}\n\t\t2\n\t{endif}\n{end}\n");
});

exports.run = async function run() {
    const root = vscode.workspace.workspaceFolders[0].uri.fsPath;
    main = await vscode.workspace.openTextDocument(vscode.Uri.file(path.join(root, "Vehicles", "Demo", "script", "main.osc")));
    lib = await vscode.workspace.openTextDocument(vscode.Uri.file(path.join(root, "Vehicles", "Demo", "script", "lib.osc")));
    await vscode.window.showTextDocument(lib);
    await vscode.window.showTextDocument(main);
    assert.strictEqual(main.languageId, "osc");

    const failures = [];
    for (const { name, body } of tests) {
        try {
            await body();
            console.log(`  ✔ ${name}`);
        } catch (error) {
            failures.push(name);
            console.log(`  ✘ ${name}\n    ${error && error.stack ? error.stack : error}`);
        }
    }

    if (failures.length > 0) {
        throw new Error(`${failures.length} of ${tests.length} tests failed`);
    }
};
