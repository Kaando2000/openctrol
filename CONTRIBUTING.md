# Contributing to Openctrol Agent

Thank you for your interest in contributing to Openctrol Agent!

## Development Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/openctrol.git
   cd openctrol
   ```

2. **Install prerequisites:**
   - .NET 8 SDK (download from https://dotnet.microsoft.com/download)
   - WiX Toolset v3.11+ (for building installer, optional for agent development)
   - Windows 10/11 or Windows Server 2016+

3. **Build the solution:**
   ```powershell
   dotnet build
   ```

4. **Run tests:**
   ```powershell
   dotnet test
   ```

## Code Style

- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and small

## Architecture

Please read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) before making significant changes. The architecture is designed to be modular and maintainable.

## Testing

- Add unit tests for new features
- Ensure all tests pass: `dotnet test`
- Test manually where automated tests aren't feasible (e.g., screen capture, input injection)

## Pull Requests

1. Create a feature branch from `main`
2. Make your changes
3. Ensure tests pass
4. Update documentation if needed
5. Submit a pull request with a clear description

## Areas for Contribution

- **Bug fixes** - Report and fix issues
- **Performance improvements** - Optimize screen capture, encoding, or network handling
- **Additional features** - Audio device support, new API endpoints, etc.
- **Documentation improvements** - Clarify, expand, or translate documentation
- **Test coverage** - Add unit tests for existing or new functionality
- **Code quality** - Refactoring, code style improvements, better error handling

## Questions?

Open an issue for discussion before starting work on major features.

