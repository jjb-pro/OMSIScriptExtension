# OMSI Script for Visual Studio Code

Language support for the scripting language of the bus simulator OMSI (`.osc` files).

## Features

### Project-aware diagnostics

The extension reads the `.bus`, `.ovh`, `.sco` and `.hum` files of the workspace and checks every script in the context of each object that loads it.

### Error reporting

The extension automatically checks for errors in the code, without the need to launch OMSI to detect them. The severity levels can be configured via the settings. Many errors also offer quick fixes.

![Screenshot of the "error reporting" feature.](assets/feat_1.webp)

### Hover documentation

The extension displays documentation for keywords, operators, system macros, system triggers and predefined variables. 

![Screenshot demonstrating the "hover documentation" feature](assets/feat_2.webp)

### Completion

The extension helps you write code quickly by suggesting blocks, operators, registers, variables, string variables, system variables, macros, system macros, constants, curves, sound triggers and trigger names from `keyboard.cfg`. 

![A GIF showing code completion](assets/feat_3.gif)

### Go to definition, find references, rename

By analyzing the entire project folder, the extension enables easy navigation and renaming across scripts, varlists and constfiles.

![Screenshot of the "find references" feature](assets/feat_4.webp)

## Settings

| Setting | Description |
|---|---|
| `omsiScript.gamePath` | OMSI installation path (detected automatically when the scripts are inside the game folder)
| `omsiScript.diagnostics.severity` | severity per diagnostic code, e.g. `{ "OSC3017": "none" }`. |
| `omsiScript.inlayHints.constants` | show constant values |
| `omsiScript.server.path` | custom language server executable |

## Usage

Open the folder that contains the object/vehicle files to enable all checks. The command *OMSI Script: Show Objects Using This Script* lists the files that reference the current script.
