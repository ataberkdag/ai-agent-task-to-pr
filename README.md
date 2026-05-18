# AI Agent Challenge

## Project Purpose
This project is a `.NET 8` Web API that turns a structured engineering task into a controlled repository automation flow. It accepts a task description, prepares an isolated workspace, clones and analyzes a target repository, asks an AI coding agent to generate code changes, validates and applies those changes safely, runs tests, and, when successful, completes the git and pull request workflow. The system supports both synchronous execution and an asynchronous in-memory queue-based execution mode.

## Technology Stack
- `.NET 8`
- `ASP.NET Core Web API`
- Layered architecture: `Api`, `Application`, `Domain`, `Infrastructure`
- `xUnit` for unit tests
- `Git CLI`
- `GitHub REST API`
- `Serilog`
- `OpenAI Chat Completions API`
- `Google Gemini API`

## AI Model
- Current repository default provider: `OpenAI`
- Current repository default OpenAI model: `gpt-5.4`
- The system also supports `Gemini` as an alternative provider
- Provider selection can be overridden with `AI_PROVIDER`
- Model selection can be overridden with `AI_MODEL`

## Setup
1. Install the prerequisites:
   - `.NET 8 SDK`
   - `Git`
2. Clone this repository.
3. Configure the required environment variables.
4. Verify access to the target repository and GitHub permissions for clone, push, and pull request creation.
5. Start the API locally.

## Environment Variables
The application uses both standard `.NET` configuration binding keys and a few convenience alias environment variables.

### Required Variables
| Variable | When it is required | Default |
| --- | --- | --- |
| `OPENAI_API_KEY` | Required when `AI_PROVIDER=OpenAI` or `Ai:Provider=OpenAI` | none |
| `GEMINI_API_KEY` | Required when `AI_PROVIDER=Gemini` or `Ai:Provider=Gemini` | none |
| `GITHUB_TOKEN` | Required for GitHub private repository clone, authenticated push, and pull request creation when `GitHub__TokenEnvironmentVariable` remains `GITHUB_TOKEN` | none |
| `RepositoryPolicy__AllowedOwners__0` | required owner user id (for example ataberkdag) | `example-company` |

### Optional Variables
| Variable | Purpose | Default |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core environment name | `Development` |
| `AI_PROVIDER` | Convenience override for `Ai:Provider` | `OpenAI` |
| `AI_MODEL` | Convenience override for `Ai:Model` | `gpt-5.4` |
| `WORKSPACE_ROOT` | Convenience override for `WorkspaceRoot` | `workspaces` |
| `Ai__OpenAiApiBaseUrl` | OpenAI base URL | `https://api.openai.com` |
| `Ai__GeminiApiBaseUrl` | Gemini base URL | `https://generativelanguage.googleapis.com` |
| `Ai__MaxContextFiles` | Maximum number of non-solution files considered for AI context | `50` |
| `Ai__MaxFileBytes` | Maximum per-file context size in bytes | `100000` |
| `Ai__MaxChangedFiles` | Maximum number of AI-generated file changes accepted | `50` |
| `Ai__MaxCriticalSignatures` | Maximum number of critical signatures included in repository analysis summary | `50` |
| `Ai__RequestTimeoutSeconds` | AI request timeout | `300` |
| `Ai__MaxTestFixAttempts` | Maximum AI-based test fix retries | `1` |
| `RepositoryPolicy__AllowedHosts__0` | First allowlisted host example | `github.com` |
| `RepositoryPolicy__DisallowedHosts__0` | First denylisted host example | empty |
| `RepositoryPolicy__DisallowedOwners__0` | First denylisted owner example | empty |
| `Git__CloneTimeoutSeconds` | Git clone timeout | `120` |
| `Git__PushTimeoutSeconds` | Git push and related git command timeout | `120` |
| `GitHub__ApiBaseUrl` | GitHub API base URL | `https://api.github.com` |
| `GitHub__TokenEnvironmentVariable` | Environment variable name used for the GitHub token | `GITHUB_TOKEN` |
| `BuildRunner__TimeoutSeconds` | Build command timeout | `180` |
| `BuildRunner__MaxOutputChars` | Maximum persisted build output length | `8000` |
| `TestRunner__TimeoutSeconds` | Test command timeout | `120` |
| `TestRunner__MaxOutputChars` | Maximum persisted test output length | `8000` |
| `AsyncExecution__QueueCapacity` | In-memory async queue capacity | `100` |
| `ExecutionReports__Directory` | Directory used to store execution report JSON files | `execution-reports` |
| `Serilog__FilePath` | Relative or absolute log directory setting | `logs` |

Notes:
- `AI_PROVIDER`, `AI_MODEL`, and `WORKSPACE_ROOT` are convenience alias variables.
- The remaining settings are read through standard `.NET` configuration binding.
- Relative `WORKSPACE_ROOT`, `ExecutionReports__Directory`, and `Serilog:FilePath` values resolve from `AppContext.BaseDirectory`, not the repository root.
- With the default settings, runtime artifacts are created next to the running app under `workspaces`, `execution-reports`, and `logs`.

## How to Run
### Local
```bash
dotnet run --project src/AiAgentChallenge.Api --urls http://localhost:8080
```

Swagger:
```text
http://localhost:8080/swagger
```

## Example Task Payload
Example task payload file:
- [task-factory-pattern.json](/samples/task-factory-pattern.json)

This payload can be sent to:
- `POST /api/tasks`
- `POST /api/tasks/async`

## Example Execution Report
Example execution report file:
- [example-execution-report.json](/samples/example-execution-report.json)

In the asynchronous flow:
1. Call `POST /api/tasks/async`
2. Read the returned `id`
3. Call `GET /api/tasks/reports/{id}` to retrieve the persisted execution report

## Architecture
The application uses a layered architecture:
- `Api`: HTTP endpoints, request/response contracts, trace id access
- `Application`: abstractions and task-level request/result models
- `Domain`: core models such as execution reports, test results, and repository analysis
- `Infrastructure`: concrete implementations for parsing, repository analysis, AI providers, validation, git, testing, queueing, and report persistence

### Synchronous and Asynchronous Execution
- `POST /api/tasks` runs the full workflow synchronously inside the request lifecycle and returns the final `ExecutionReport`.
- `POST /api/tasks/async` validates and accepts the request, places it into an in-memory bounded queue, and returns immediately with a lookup id.
- `GET /api/tasks/reports/{id}` reads the persisted execution report JSON file associated with that id.

## Pipeline Flow
The system executes a step-based pipeline. Each step has a specific responsibility and passes its output forward to the next stage.

1. `Request validation`
   - What it does: checks that required input fields such as task id, title, and description are present.
   - How it works: request-level validation runs before the expensive workflow begins, so malformed tasks fail early.

2. `Task parsing`
   - What it does: extracts repository URL, branch, requirement, and acceptance criteria from the task description.
   - How it works: a rule-based parser reads the structured description format and converts it into a normalized execution request.

3. `Repository policy validation`
   - What it does: verifies that the requested repository is allowed to be processed.
   - How it works: the system checks URL shape, HTTPS usage, host allowlist/denylist rules, and owner allowlist/denylist rules before any clone operation starts.

4. `Workspace creation`
   - What it does: creates an isolated working directory for the current run.
   - How it works: every execution gets its own path-safe task-specific workspace, preventing overlap between separate runs of the same or different tasks.

5. `Repository clone`
   - What it does: clones the target repository at the requested base branch.
   - How it works: the service calls `git clone` through a structured process abstraction with timeouts and explicit error handling for branch, auth, and clone failures.

6. `Repository analysis`
   - What it does: inspects the codebase to understand its ecosystem and locate the files most relevant to the task.
   - How it works: the analyzer combines generic relevant-file scoring with framework-specific analysis, detects language/framework/build/test information, and produces `ProjectFiles`, `RelevantFiles`, `ExistingTestFiles`, API endpoint hints, and symbol metadata.
   - `.NET/C#` detail: relevant files are expanded beyond filename heuristics using referenced-type expansion plus a dependency-walk index that follows injected services, DTOs, factories, constructed concrete types, and related base-type families.
   - `.NET/C#` detail: repository metadata such as `Available Test Libraries`, `TargetFramework`, `TargetFrameworks`, and `LangVersion` is also extracted to improve prompt quality and downstream validation.

7. `Repository maintenance baseline`
   - What it does: captures repository-specific metadata when the detected ecosystem needs it for later consistency checks.
   - How it works: for example, in `.NET` repositories this may include solution membership baselines so that newly added projects can be synchronized correctly later in the pipeline.

8. `Agent context build`
   - What it does: prepares the subset of repository data that will be sent to the AI model.
   - How it works: the system selects solution files, relevant source files, project files, and existing test files; excludes sensitive or binary files; applies secret redaction; enforces a per-file byte limit; and builds a compact repository analysis summary instead of sending the whole repository.
   - Context summary detail: the generated summary can include `Available Test Libraries`, `Target Framework`, `LangVersion`, `Detected API Endpoints`, and `Critical Signatures` so the model can use repository-specific call shapes and test stack conventions.

9. `AI change generation`
   - What it does: asks the selected AI provider to propose code changes for the task.
   - How it works: the provider receives a strict system prompt, repository context, and a structured JSON output contract so that the result is machine-validated rather than free-form text.

10. `AI change validation`
   - What it does: checks whether the AI output is safe, relevant, and structurally usable.
   - How it works: the validator enforces path safety, operation rules, sensitive file protection, changed-file limits, unchanged-file filtering, and language-specific constraints where needed.

11. `Change application`
   - What it does: writes validated changes into the cloned repository.
   - How it works: file updates are applied only after validation, and all paths are resolved safely so writes cannot escape the repository root.

12. `Repository maintenance synchronization`
   - What it does: updates ecosystem-specific repository metadata if the generated changes require it.
   - How it works: when the detected stack requires project membership or workspace metadata synchronization, the pipeline updates that state. For example, in `.NET` repositories, newly created project files may need to be added to an existing solution.

13. `Test execution`
   - What it does: runs the repository's detected or resolved test command after applying the AI changes.
   - How it works: the test command is resolved through an allowlisted command strategy and executed through a safe process abstraction with timeout and output capture.

14. `AI fix retry`
   - What it does: optionally gives the AI one or more chances to fix failing tests.
   - How it works: failing build and test output is passed back to the AI with a narrower corrective prompt. Build failures and test failures are directed differently, and `.NET` fixes are guided by repository test stack rules, minimal-test discipline, and existing-library reuse rules.

15. `Git diff, branch, commit, and push`
   - What it does: prepares the repository for publication once the final test state is acceptable.
   - How it works: the system gathers changed files and diff summary information, creates a branch, commits the changes, and pushes the branch through structured git commands.

16. `Pull request create or reuse`
   - What it does: creates a new pull request or returns an existing matching pull request when appropriate.
   - How it works: GitHub API calls are used to check for existing open PRs and create a new one only when needed.

17. `Execution report persistence`
   - What it does: saves the outcome of the run in a machine-readable report.
   - How it works: the final `ExecutionReport` is returned directly for synchronous calls and is also persisted as JSON so asynchronous runs can be retrieved later by id.

## AI Agent Flow
Provider selection is handled by the provider routing layer:
- `AI_PROVIDER=OpenAI` routes requests to the OpenAI implementation
- `AI_PROVIDER=Gemini` routes requests to the Gemini implementation

The AI workflow follows these steps:
1. The system selects relevant repository files instead of sending the full repository.
2. Smart context selection limits what is sent using relevant file scoring, framework analysis, `.NET` dependency-walk expansion, maximum context file count, and per-file size limits.
3. Secret redaction is applied before any repository content is sent to the model.
4. Sensitive, binary, ignored, or repository-escaping paths are excluded from model context and from file write operations.
5. A strict system prompt instructs the model to return JSON only.
6. The model returns file-level changes using `create` and `modify` operations.
7. Unchanged file results are silently filtered out.
8. If formatting or test-fix recovery is needed, additional AI calls are made with narrower intent.
9. In `.NET` repositories, prompts are enriched with repository test stack information, available test libraries, detected API endpoints, and critical signatures.
10. `.NET` test generation is constrained by minimal-test rules, existing test library reuse rules, and a default preference against thin controller unit tests.
11. The final result passes through validation and safe file application before it affects the cloned repository.

How updates, additions, and removals are handled:
- New files are produced through `create`
- Existing files are updated through `modify`
- Sensitive files and repository-escaping paths are rejected
- The contract is based on final file content rather than line-based diff patches
- Language-specific prompt rules are applied when the detected stack requires them, such as explicit import/using guidance, package/project references, solution membership expectations, and test stack reuse
- Execution reports and PR descriptions capture the final changed files, test result summary, and AI model usage metadata

## Security Approach
- Repository allowlist and denylist rules are enforced
- Only HTTPS repository URLs are accepted
- GitHub-hosted repository clones can use `GITHUB_TOKEN` for scoped authenticated access
- Every run uses an isolated workspace
- Sensitive files are excluded from AI context
- Secret redaction is applied before model calls
- AI output is structurally validated before any write
- The safe file applier blocks writes outside the repository
- Test commands are restricted through an allowlist
- Task descriptions and repository files are treated as untrusted input to reduce prompt injection risk
- Serilog is used for operational logging

## Known Limitations
- The async queue is in memory, so queued work is lost if the process restarts
- Report lookup is file-based; there is no separate persistent execution store
- The current authenticated clone path is GitHub-specific
- There is no human approval gate before git finalization
- Git provider support is currently GitHub-focused

## Test Failure Behavior
- Tests run after the initial AI-generated changes are applied
- If tests fail, the system can attempt AI-based corrective retries up to the configured retry limit
- AI-generated fixes go through the same validation and application path as the first result
- If tests still fail after the retry flow:
  - git finalization stops
  - no push is performed
  - no pull request is created
  - the execution is reported as failed

## Production Improvements
- Replace the in-memory queue with a persistent queue
- Add a persistent execution store for status and report lookup
- Add authenticated clone support for git providers beyond GitHub
- Introduce a stronger sandbox for test execution
- Add a human approval gate before git finalization
- Improve rate limiting and retry policies
- Expand AI token and cost visibility
- Add stronger repository indexing and search
- Support more git hosting providers
- Improve secret scanning and policy enforcement
- Add richer observability, metrics, and tracing
