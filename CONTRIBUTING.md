# Contributing to Space.Map

Thank you for your interest in contributing to **Space.Map**! We welcome bug reports, feature suggestions, documentation improvements, and pull requests.

## Development Setup

- Windows 10/11
- .NET 10 SDK or higher
- Visual Studio 2022 (v17.13+) / Visual Studio Code / JetBrains Rider

### Build & Test

```powershell
# Restore & build solution
dotnet build Space.Map.slnx

# Run unit tests
dotnet test Space.Map.slnx

# Run WPF application
dotnet run --project src/Space.Map.App/Space.Map.App.csproj
```

## Architectural Guidelines

Space.Map follows the **Deep Module** philosophy:
- `src/Space.Map.Core`: Standalone, headless, high-leverage disk scanning engine and models. Must have **zero WPF/UI dependencies**.
- `src/Space.Map.App`: Fluent WPF desktop client using `WPF-UI` and `CommunityToolkit.Mvvm`.
- `tests/Space.Map.Core.Tests`: Unit tests for core scanning, edge cases, cancellation, and size calculations.

## Pull Request Guidelines

1. Ensure all unit tests pass locally before opening a PR (`dotnet test Space.Map.slnx`).
2. Adhere to code style defined in `.editorconfig`.
3. If adding user-facing text, provide entries in both `Strings.en-US.xaml` and `Strings.zh-CN.xaml`.
