# Repository Guidelines

## Project Structure & Module Organization

This is an unpackaged WinUI 3 desktop app targeting .NET 8 and Windows 10 19041 or later. `App.xaml` and `App.xaml.cs` own startup and application lifetime. `MainWindow.xaml` defines the island UI, while `MainWindow.xaml.cs` handles window positioning, animation, tray behavior, and native interop. Keep display records in `Models/`, Windows API and integration wrappers in `Services/`, and bindable state and commands in `ViewModels/`. Shared colors, dimensions, and styles belong in XAML resources rather than being duplicated in code. `PROJECT_PLAN.md` records scope and important implementation decisions.

## Build, Test, and Development Commands

- `dotnet restore` - restore Windows App SDK and NAudio packages.
- `dotnet build -c Debug -p:Platform=x64` - compile the development build.
- `dotnet run -c Debug -p:Platform=x64` - launch the unpackaged app locally.
- `dotnet build -c Release -p:Platform=x64` - perform the release validation expected by the project plan.

Run these commands on Windows with the .NET 8 SDK and required Windows App SDK tooling installed. Generated `bin/`, `obj/`, and `.vs/` content must remain untracked.

## Coding Style & Naming Conventions

Use four-space indentation for C# and consistent indentation for XAML. Nullable reference types and implicit usings are enabled. Follow standard .NET naming: PascalCase for types, properties, methods, and events; camelCase for parameters and locals; `_camelCase` for private fields. Use `Async` suffixes for asynchronous methods. Keep services thin, route UI-bound callbacks through `DispatcherQueue`, and avoid putting platform API access in views. Add XML summaries or focused comments for new complex logic, especially threading, audio processing, SSE parsing, or Win32 interop.

## Testing Guidelines

There is currently no automated test project or coverage threshold. Every change must at least pass the relevant x64 build. For UI or integration changes, smoke-test startup, expand/collapse behavior, tray exit, focus handling, and the affected media, power, audio, or OpenCode flow. When adding tests, create a separate `WindowsDynamicIsland.Tests` project and name test files after the subject, such as `IslandViewModelTests.cs`.

## Commit & Pull Request Guidelines

Git history is not included in this checkout, so no repository-specific commit pattern can be inferred. Use short imperative subjects, for example `Fix media callback dispatching`, and keep each commit focused. Pull requests should explain the user-visible result, list verification performed, link relevant issues, and include screenshots or a short recording for visual changes. Call out untested Windows versions, hardware-dependent behavior, or fallback paths.
