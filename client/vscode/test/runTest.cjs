const path = require("path");
const os = require("os");
const fs = require("fs");
const { runTests } = require("@vscode/test-electron");

async function main() {
    const userData = fs.mkdtempSync(path.join(os.tmpdir(), "osc-ext-test-"));
    const workspace = fs.mkdtempSync(path.join(os.tmpdir(), "osc-ext-workspace-"));
    fs.cpSync(path.resolve(__dirname, "fixtures"), workspace, { recursive: true });
    await runTests({
        extensionDevelopmentPath: path.resolve(__dirname, ".."),
        extensionTestsPath: path.resolve(__dirname, "suite.cjs"),
        vscodeExecutablePath: process.env.VSCODE_EXECUTABLE || undefined,
        launchArgs: [
            workspace,
            "--disable-extensions",
            "--disable-workspace-trust",
            `--user-data-dir=${userData}`,
        ],
    });
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
