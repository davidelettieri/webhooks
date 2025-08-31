# Webhooks - GitHub Copilot Instructions

**Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.**

This repository provides a standard implementation for webhooks in .NET 9.0, including both publishers and receivers with CosmosDB storage support and .NET Aspire orchestration.

## Working Effectively

### Prerequisites and Setup
- Install .NET 9.0 SDK:
  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 9.0.102
  export PATH="$HOME/.dotnet:$PATH"
  ```
- Verify installation: `dotnet --version` should show `9.0.102` or higher

### Build and Test Process
- **Restore packages:** `dotnet restore Webhooks.sln` -- takes approximately 2-53 seconds (2s subsequent, 53s cold). NEVER CANCEL.
- **Build:** `dotnet build Webhooks.sln --configuration Release --no-restore` -- takes approximately 5-17 seconds. NEVER CANCEL.
- **Test:** `dotnet test Webhooks.sln --configuration Release --no-build` -- takes approximately 90 seconds due to CosmosDB emulator containers. NEVER CANCEL. Set timeout to 120+ seconds.

### Running Sample Applications

#### MinimalAPI Webhook Receiver
- **Start receiver:** `dotnet run --configuration Release` in `samples/MinimalApis.Receivers/`
- **Endpoint:** http://localhost:5033
- **Health check:** `curl http://localhost:5033/` should return "Running!"
- **Webhook endpoint:** POST to `http://localhost:5033/webhooks/receive`

#### Worker Service Publisher
- **Start publisher:** `dotnet run --configuration Release` in `samples/WorkerService.Publisher/`
- **Behavior:** Publishes webhooks every second to `http://localhost:5033/webhooks/receive`
- **Testing:** Start receiver first, then publisher to see end-to-end webhook delivery
- **Payload:** Generates JSON with random GUIDs for id field

#### .NET Aspire AppHost (Orchestration)
- **Start orchestrated setup:** `dotnet run --configuration Release` in `samples/Webhooks.AppHost/`
- **Note:** May have Kubernetes connectivity issues in some environments. Use individual apps if AppHost fails.

## Validation

### Manual Testing Requirements
- **ALWAYS** run through complete webhook scenarios after making changes:
  1. Start the MinimalAPI receiver: `cd samples/MinimalApis.Receivers && dotnet run`
  2. Verify receiver health: `curl http://localhost:5033/`
  3. Test webhook reception using the sample in `samples/MinimalApis.Receivers/Samples.http`
  4. Verify webhook validation middleware works correctly
- **ALWAYS** run all tests after code changes: `dotnet test Webhooks.sln`
- **ALWAYS** validate builds pass on all projects: `dotnet build Webhooks.sln`

### CosmosDB Testing
- Tests use Docker testcontainers with CosmosDB emulator
- Requires Docker to be running and accessible
- Tests in `Webhooks.Receivers.Storage.CosmosDb.Tests` take 85+ seconds due to container initialization
- **NEVER CANCEL** these tests - container startup/teardown is expected to be slow

### Pre-commit Validation
- **Install pre-commit:** `pip3 install pre-commit` (if not available)
- **Run hooks:** `pre-commit run --all-files`
- **Hooks check:** trailing whitespace, end-of-file-fixers, YAML syntax, and large files
- **Note:** Hooks may modify files automatically - review changes before committing

## Key Projects and Structure

### Core Libraries
- **`src/Webhooks.Publishers/`** - Webhook publishing functionality with HMAC signature generation
- **`src/Webhooks.Receivers/`** - Webhook receiving and validation middleware
- **`src/WebHooks.Receivers.Storage/`** - Storage abstraction for webhook payloads
- **`src/Webhooks.Receivers.Storage.CosmosDb/`** - CosmosDB implementation for storage

### Sample Applications  
- **`samples/MinimalApis.Receivers/`** - ASP.NET Core minimal API webhook receiver
- **`samples/WorkerService.Publisher/`** - Background service that publishes webhooks
- **`samples/Webhooks.AppHost/`** - .NET Aspire orchestration project
- **`samples/Webhooks.ServiceDefaults/`** - Shared Aspire service configuration

### Test Projects
- **`tests/Webhooks.Publishers.Tests/`** - Unit tests for publisher functionality
- **`tests/Webhooks.Receivers.Tests/`** - Unit tests for receiver middleware
- **`tests/Webhooks.Receivers.Storage.CosmosDb.Tests/`** - Integration tests with CosmosDB emulator
- **`tests/Webhooks.Tests.Common/`** - Shared test utilities

## Common Tasks

### Repository Root Structure
```
.
├── .github/                    # GitHub Actions workflows and this file
├── .gitignore                  # Standard .NET gitignore
├── .pre-commit-config.yaml     # Pre-commit hooks configuration
├── Directory.Build.props       # MSBuild properties (TreatWarningsAsErrors=true)
├── LICENSE                     # MIT License
├── README.md                   # Basic project description
├── Webhooks.sln               # Solution file with all projects
├── samples/                   # Example applications
├── src/                       # Core library projects
└── tests/                     # Test projects
```

### CI/CD Pipeline
- **GitHub Actions:** `.github/workflows/ci.yml`
- **Build timeout:** 10 minutes (sufficient for all operations)
- **Runs on:** Ubuntu latest with .NET 9.0
- **Steps:** Restore → Build → Test with coverage → Publish artifacts
- **Triggers:** Push to main, PRs to main

### Configuration Details
- **Target Framework:** .NET 9.0
- **Warnings as Errors:** Enabled in Directory.Build.props
- **Test Framework:** xUnit with testcontainers for integration tests
- **Sample webhook key:** `whsec_test_123` (for development only)

## Troubleshooting

### Common Issues
- **SDK Version Error:** Ensure .NET 9.0 SDK is installed and in PATH
- **CosmosDB Test Failures:** Verify Docker is running and accessible
- **Aspire AppHost Issues:** Fall back to running individual sample apps
- **Long Test Times:** Expected - CosmosDB container tests take 85+ seconds
- **Build Warnings:** All warnings are treated as errors in this project

### Performance Expectations
- **Package Restore:** ~2 seconds (subsequent), ~53 seconds (cold/first time)
- **Full Build:** ~5-17 seconds depending on changes
- **Full Test Suite:** ~90 seconds (including CosmosDB container tests)
- **Simple App Startup:** ~3-4 seconds

### Testing Webhook Functionality
1. Use the provided `Samples.http` file in the receiver project for guided testing
2. **Manual signature testing:** Generate proper HMAC-SHA256 signatures:
   ```python
   import hmac, hashlib, base64, time
   msg_id = 'evt_0001'
   timestamp = int(time.time())
   payload = '{"hello": "world"}'
   key = 'whsec_test_123'
   data = f'{msg_id}.{timestamp}.{payload}'
   signature = base64.b64encode(hmac.new(key.encode(), data.encode(), hashlib.sha256).digest()).decode()
   # Use headers: webhook-id: {msg_id}, webhook-signature: t={timestamp},v1={signature}
   ```
3. Test signature validation with different keys to ensure rejection works
4. Verify payload storage in CosmosDB (when using storage tests)
5. Check middleware rejection of invalid signatures and malformed headers
6. **Note:** Webhook validation includes timestamp checking - use current timestamps for testing

**CRITICAL REMINDERS:**
- **NEVER CANCEL** builds or tests - they may take significant time but will complete
- **ALWAYS** validate your changes with manual testing scenarios
- **ALWAYS** run the full test suite before submitting changes
- **ALWAYS** ensure Docker is available when running CosmosDB-related tests