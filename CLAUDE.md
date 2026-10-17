# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

A Blazor WebAssembly proof of concept with an ASP.NET Core API backend. The structure below is the intended design. No projects have been scaffolded yet, so update this file with real build/run commands once they exist.

## Architecture
- Clean Architecture. Dependencies point inward only:
  - `Domain`: entities, value objects, domain logic. No references to other projects or frameworks.
  - `Application`: use cases, interfaces (ports), DTOs. References Domain only.
  - `Infrastructure`: EF Core, external services, implementations of Application interfaces.
  - `Client` (Blazor WASM): UI only. Talks to the API over HTTP, never references Infrastructure.
  - `Api` (ASP.NET Core host): composition root, DI wiring, endpoints.
- If a change would add a reference that breaks this direction, stop and ask.

## Git workflow
- Branches: `main` (releases only) and `dev` (integration). Never commit directly to either.
- Every piece of work goes on a branch from `dev`: `feature/<short-name>`, `fix/<short-name>`.
- Before starting work, check the current branch. If it's `main` or `dev`, create a feature branch first.
- Commits use Conventional Commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`, with optional scope, e.g. `feat(client): add login page`.
- Small, focused commits. Don't merge; the user reviews and merges to `dev` themselves.

## C# conventions
- Follow Microsoft's .NET coding conventions and the repo's `.editorconfig`.
- Nullable reference types enabled; no warnings left unresolved.
- File-scoped namespaces, `async` all the way with `Async` suffix, pass `CancellationToken` through.
- Dependency injection via constructors; no service locator.
- Tests: NUnit + Moq. Write or update tests with each feature. Run `dotnet build` and `dotnet test` before committing.
