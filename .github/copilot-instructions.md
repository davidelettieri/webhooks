# Webhooks Repository Instructions

**Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.**

This repository provides a standard implementation for webhooks in .NET, including both publishers and receivers with Azure CosmosDB storage integration. The solution uses .NET Aspire for distributed application orchestration and includes comprehensive samples.

## Working Effectively

### Prerequisites and Environment Setup
- Install .NET 9.0 SDK (REQUIRED - project targets .NET 9.0, not .NET 8.0):
  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 9.0.100
  export PATH="/home/runner/.dotnet:$PATH"
  ```
- Verify installation: `dotnet --version` should show `9.0.100` or higher
- Docker is required for CosmosDB integration tests (uses testcontainers)

### Build and Test Process
- **NEVER CANCEL builds or tests** - they may take longer than expected
- Bootstrap the repository:
  ```bash
  cd /path/to/Webhooks
  dotnet restore Webhooks.sln  # Takes ~54 seconds. NEVER CANCEL.
  ```
- Build the solution:
  ```bash
  dotnet build Webhooks.sln --configuration Release --no-restore  # Takes ~18 seconds
  ```
- Run all tests:
  ```bash
  dotnet test Webhooks.sln --configuration Release --no-build --logger "console;verbosity=normal"
  # Takes ~91 seconds (1.5 minutes). NEVER CANCEL. Set timeout to 120+ seconds.
  # CosmosDB tests use Docker testcontainers which take time to start.
  ```

### Running the Application

#### Webhook Receiver (Minimal API)
- Run the webhook receiver sample:
  ```bash
  cd samples/MinimalApis.Receivers
  dotnet run  # Runs on http://localhost:5033
  ```
- Test basic functionality:
  ```bash
  curl http://localhost:5033/  # Should return "Running!"
  ```

#### Webhook Publisher (Worker Service)
- Run the webhook publisher sample:
  ```bash
  cd samples/WorkerService.Publisher
  dotnet run
  ```

#### Aspire Application Host
- The full distributed application can be run via:
  ```bash
  cd samples/Webhooks.AppHost
  dotnet run
  ```
- **Note**: Aspire may have environment limitations in some sandboxed environments

## Validation Scenarios

### Critical Validation Steps
**ALWAYS manually validate webhook functionality after making changes:**

1. **Webhook Receiver Validation**:
   ```bash
   # Start receiver
   cd samples/MinimalApis.Receivers && dotnet run &

   # Test basic endpoint
   curl http://localhost:5033/

   # Test webhook endpoint (should show validation warning)
   curl -X POST http://localhost:5033/webhooks/receive \
     -H "Content-Type: application/json" \
     -H "Webhook-Id: test" \"
     -H "Webhook-Timestamp: $(date +%s)" \
     -H "Webhook-Signature: v1=test" \
     -d '{"message": "test webhook"}'
   ```

2. **Build Validation**:
   - Always run full build before committing: `dotnet build Webhooks.sln --configuration Release`
   - Verify no compilation warnings (TreatWarningsAsErrors=true)

3. **Test Validation**:
   - Run all tests: `dotnet test Webhooks.sln --configuration Release --no-build`
   - **CRITICAL**: CosmosDB tests take 80+ seconds due to Docker container startup
   - Tests include: Publishers (1 test), Receivers (9 tests), CosmosDB Integration (4 tests)

## Project Structure

### Core Libraries
- **`src/Webhooks.Receivers`** - Core webhook receiving functionality, signature validation
- **`src/Webhooks.Publishers`** - Core webhook publishing functionality
- **`src/WebHooks.Receivers.Storage`** - Storage abstractions for webhook payloads
- **`src/Webhooks.Receivers.Storage.CosmosDb`** - Azure CosmosDB storage implementation

### Sample Applications
- **`samples/MinimalApis.Receivers`** - ASP.NET Core minimal API webhook receiver
- **`samples/WorkerService.Publisher`** - Background service webhook publisher
- **`samples/Webhooks.AppHost`** - .NET Aspire application orchestration
- **`samples/Webhooks.ServiceDefaults`** - Common Aspire service configurations

### Test Projects
- **`tests/Webhooks.Publishers.Tests`** - Publisher functionality tests
- **`tests/Webhooks.Receivers.Tests`** - Receiver and validation middleware tests
- **`tests/Webhooks.Receivers.Storage.CosmosDb.Tests`** - CosmosDB integration tests
- **`tests/Webhooks.Tests.Common`** - Shared test utilities

## Configuration and Key Files

### Important Configuration Files
- **`Webhooks.sln`** - Main solution file with 12 projects
- **`Directory.Build.props`** - Sets TreatWarningsAsErrors=true for all projects
- **`.github/workflows/ci.yml`** - CI pipeline (uses .NET 9.0, timeout: 10 minutes)
- **`samples/MinimalApis.Receivers/appsettings.json`** - Webhook validation key: "whsec_test_123"

### Launch Settings
- Webhook receiver runs on: `http://localhost:5033` (HTTP) or `https://localhost:7260` (HTTPS)
- Applications use standard ASP.NET Core launch profiles

## Timing Expectations and Warnings

| Operation | Expected Time | Timeout Setting | Critical Notes |
|-----------|---------------|-----------------|----------------|
| `dotnet restore` | ~54 seconds | 120+ seconds | **NEVER CANCEL** - Downloads many packages |
| `dotnet build` | ~18 seconds | 60+ seconds | Fast after restore |
| `dotnet test` | ~91 seconds | 180+ seconds | **NEVER CANCEL** - CosmosDB container startup |
| CosmosDB Tests Only | ~80 seconds | 150+ seconds | Docker testcontainer initialization |
| CI Pipeline Total | <10 minutes | 10 minutes | Full restore + build + test |

## Common Development Tasks

### Making Changes to Webhook Validation
- Key files: `src/Webhooks.Receivers/SymmetricKeyWebhookValidationMiddleware.cs`
- Always test with: `tests/Webhooks.Receivers.Tests/SymmetricKeyWebhookValidationMiddlewareTests.cs`
- Validation logic uses HMAC-SHA256 with configurable keys

### Adding Storage Implementations
- Implement interfaces in: `src/WebHooks.Receivers.Storage/`
- Follow pattern from: `src/Webhooks.Receivers.Storage.CosmosDb/`
- Add integration tests with testcontainers if external dependencies

### CI/CD Integration
- **Always run locally before pushing**: `dotnet test Webhooks.sln --configuration Release`
- Pipeline automatically runs on push to main and PR creation
- Uses Ubuntu latest with .NET 9.0.x SDK
- Includes test result publishing and code coverage collection

### Copilot instructions
- Whenever a new project is added, update this instruction file to reflect the new structure and any specific build/test instructions.

## Troubleshooting

### Common Issues
1. **Build fails with NETSDK1045**: Install .NET 9.0 SDK (not 8.0)
2. **Tests timeout**: Increase timeout, Docker may be slow to start containers
3. **Aspire fails to start**: Environment may not support Aspire orchestration - use individual samples instead
4. **Webhook validation fails**: Check `Webhook-Signature` header format and signing key, check `Webhook-Timestamp` for clock skew, check required `Webhook-Id` header

### Quick Diagnostics
```bash
# Check .NET version
dotnet --version

# Verify solution structure
dotnet sln list

# Check for compilation issues
dotnet build Webhooks.sln --verbosity normal

# Run specific test project
dotnet test tests/Webhooks.Receivers.Tests --verbosity normal
```

Remember: This is a webhook implementation with real security validation - always test end-to-end scenarios when making changes to signing or validation logic.
