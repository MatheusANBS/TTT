# Contributing to TTT (Advanced Memory Tool)

First off, thank you for considering contributing to TTT! It's people like you that make TTT such a great tool for the community.

## Where do I go from here?

If you've noticed a bug or have a feature request, make sure to check our [Issues](../../issues) to see if someone else has already created a ticket. If not, go ahead and [create one](../../issues/new/choose)!

## Setting up your environment

1. Fork the repository
2. Clone your fork: `git clone https://github.com/your-username/TTT.git`
3. Make sure you have the [.NET 8 SDK](https://dotnet.microsoft.com/download) installed.
4. Ensure you have an appropriate IDE like Visual Studio 2022, JetBrains Rider, or VS Code with the C# Dev Kit.
5. Open `TTT/TTT.csproj` (or the folder) in your IDE.
6. Build and run the project locally.

### Technologies Used

- **C# 12 / .NET 8**
- **Avalonia UI** for the cross-platform presentation layer.

## Making Changes

- Create a new branch from `main`: `git checkout -b my-feature-branch`
- Make your changes.
- Please follow the existing code style (see existing files as a reference).
- Test your changes locally. If you add a new feature, make sure it doesn't break existing scanning, debugging, or editing functionality.

## Pull Request Process

1.  Commit your changes with a clear and descriptive commit message.
2.  Push your branch to your fork: `git push origin my-feature-branch`
3.  Open a Pull Request against the `main` branch.
4.  Fill out the required PR template.
5.  Wait for a code review!

## Code of Conduct

Please note that this project is released with a [Contributor Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.
