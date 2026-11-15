# AI Agent Project Guidelines Template

Replace the bracketed prompts with your project's decisions. Remove inapplicable
guidance explicitly; the headings are a starting point, not mandatory fields.
Do not include credentials, personal data, or confidential sample records.
Saving this document supplies project guidance; it does not authorize an agent
to change files outside an approved task specification.

## 1. Project Overview

[Describe the business problem, intended users, outcomes, and scope.]

## 2. Technology Stack

[List approved runtimes, frameworks, versions, database providers, and tools.]

## 3. Architecture Rules

### Required Layers

[Identify applicable layers and their responsibilities.]

### Dependency Rules

#### Domain

[Specify domain dependencies and prohibited infrastructure references.]

#### Application

[Specify use cases, contracts, orchestration, and permitted dependencies.]

#### Infrastructure

[Specify persistence, integrations, and implementation boundaries.]

#### API

[Specify transport, authorization, and application-service boundaries.]

#### Blazor

[Specify UI responsibilities and prohibit server-only logic in browser code.]

## 4. AI Agent Coding Rules

### General Requirements

[Define coding conventions, compatibility expectations, and review requirements.]
Limit changes to the task's approved implementation, test, and documentation
paths. Stop and request a scope amendment if additional files are necessary.
Do not silently expand scope, refactor unrelated code, or claim unrun tests pass.

## 5. Solution Structure

[Show the actual projects and permitted project-reference directions.]

## 6. Domain Modeling Rules

### Entities

[Define identity, invariants, lifecycle, and aggregate boundaries.]

### Value Objects

[Define equality, immutability, validation, and persistence conventions.]

## 7. Database Standards

### Database Name

[Specify names per environment without credentials.]

### Schema Organization

[Specify schemas or provider-appropriate organization.]

### Naming Conventions

#### Tables

[Specify table naming.]

#### Primary Keys

[Specify key types and naming.]

#### Foreign Keys

[Specify naming, referential integrity, and delete behavior.]

### Audit Columns

[Specify timestamps, actors, retention, and concurrency tokens.]

## 8. Example Domain Model

### Customer

[Provide an illustrative customer entity, or mark not applicable.]

### Product

[Provide an illustrative product entity, or mark not applicable.]

### Order

[Provide an illustrative order aggregate and relationships, or mark not applicable.]

## 9. Sample Data

### Customer Sample

[Provide fictional, non-sensitive customer examples.]

### Product Sample

[Provide fictional product examples.]

### Order Sample

[Provide fictional orders with valid relationships.]

## 10. Entity Framework Rules

### Entity Configurations

[Specify configuration location, conversions, and relationship rules.]

### Migrations

[Specify generation/review commands and supported-provider verification.]

### DbContext Standards

[Specify lifetime, transaction boundaries, and tenant scoping.]

### Query Performance Guidelines

[Specify projections, tracking, pagination, and N+1 prevention.]

## 11. ASP.NET Web API Standards

### Endpoint Design

[Specify routes, HTTP methods, pagination, and idempotency.]

### Request/Response Contracts

[Specify DTOs, serialization, validation, and compatibility.]

### Problem Details

[Specify safe error contracts and correlation information.]

### Versioning

[Specify versioning and deprecation policy.]

### Authentication & Authorization

[Specify authentication, permissions, and resource/tenant access checks.]

## 12. Blazor Standards

### Component Design

[Specify component responsibilities, parameters, and disposal.]

### Page Routing

[Specify routes, navigation, and authorization.]

### State Management

[Specify ownership, loading/error states, and stale-response handling.]

### Forms and Validation

[Specify user feedback and server-side validation.]

## 13. Fluent UI Standards

### Component Usage

[Specify approved library version and component patterns.]

### Design Language

[Specify tokens, layout, responsive behavior, and themes.]

### Accessibility Requirements

[Specify keyboard access, labels, contrast, focus, and alternatives to diagrams.]

## 14. Aspire Standards

### AppHost Responsibilities

[Specify orchestration and worktree isolation.]

### ServiceDefaults Configuration

[Specify shared resilience, telemetry, and health-check behavior.]

### Service Discovery

[Specify endpoint discovery rather than hard-coded addresses.]

### Telemetry and Observability

[Specify required resource logs, traces, and metrics.]

## 15. Dependency Injection Standards

### Keyed Services

[Specify when keyed registrations are appropriate.]

### Multiple Implementations

[Specify selection and registration rules.]

### Service Lifetimes

[Specify scoped/singleton/transient constraints.]

### Registration Conventions

[Specify registration locations and naming.]

## 16. Configuration Management

### appsettings

[Specify safe defaults and environment overrides.]

### Environment Variables

[Specify configuration names and precedence.]

### Secret Management

[Specify approved secret stores; never insert real secret values.]

### Options Pattern

[Specify typed options and validation.]

## 17. Logging Standards

### Structured Logging

[Specify message templates and safe structured fields.]

### Log Levels

[Specify level selection and actionable errors.]

### Correlation IDs

[Specify propagation across requests and integrations.]

### Audit Logging

[Specify audited actions, access control, and retention.]

## 18. Security Rules

### Authentication

[Specify identity provider and token/cookie validation.]

### Authorization

[Specify roles, ownership, tenant checks, and deny-by-default behavior.]

### Input Validation

[Specify trust boundaries, size limits, and output encoding.]

### Secrets Management

[Specify credential handling, rotation, and log redaction.]

### OWASP Requirements

[Specify applicable OWASP guidance and verification.]

## 19. Exception Handling Standards

### Global Exception Handling

[Specify handling boundaries and logging; avoid silent failures.]

### ProblemDetails

[Specify safe user-visible errors without internal secrets.]

### Retry Policies

[Specify transient failure classification, cancellation, and idempotency.]

## 20. Validation Standards

### FluentValidation

[Specify usage, or mark not applicable rather than introducing a dependency.]

### Business Rule Validation

[Specify invariant enforcement and conflict reporting.]

### API Validation

[Specify request validation and related-resource ownership checks.]

## 21. Data Access Standards

### Entity Framework Core

[Specify repositories or other existing data-access patterns.]

### Query Optimization

[Specify budgets, query measurement, and indexes.]

### Transactions

[Specify atomic changes and external-event publication ordering.]

### Concurrency Control

[Specify tokens, conflicts, and retry behavior.]

## 22. SQL Server Standards

### Database Design

[Specify SQL Server rules, or mark not applicable for other providers.]

### Indexing

[Specify index selection and verification.]

### Views

[Specify allowed usage and migration ownership.]

### Stored Procedures

[Specify allowed usage and contract/testing requirements.]

### Performance Requirements

[Specify measurable provider-specific budgets.]

## 23. OpenAPI Standards

### Swagger Configuration

[Specify exposure by environment and authentication.]

### Operation Documentation

[Specify request, result, error, and authorization documentation.]

### Example Requests and Responses

[Provide realistic non-sensitive examples.]

## 24. Testing Standards

### Unit Testing

[Specify framework, naming, and focused behavioral tests.]

### Integration Testing

[Specify database providers, integration doubles, and disposable resources.]

### Functional Testing

[Specify critical user journeys and browser/accessibility verification.]

### Test Coverage Requirements

[Specify meaningful coverage requirements without inventing a threshold.]

## 25. Performance Standards

### API Performance

[Specify latency/throughput budgets and measurement environment.]

### Database Performance

[Specify query budgets and representative datasets.]

### UI Performance

[Specify rendering/responsiveness budgets and target devices.]

### Caching

[Specify ownership, tenant isolation, expiry, and invalidation.]

## 26. Observability Standards

### OpenTelemetry

[Specify instrumentation and export configuration.]

### Metrics

[Specify required metrics without sensitive/high-cardinality labels.]

### Tracing

[Specify propagation and sensitive payload exclusions.]

### Health Checks

[Specify readiness/liveness and dependency checks.]

## 27. CI/CD Standards

### Build Pipeline

[Specify exact restore/build commands.]

### Deployment Pipeline

[Specify environments, approvals, and migration execution.]

### Quality Gates

[Specify tests, formatting, security, and task file-scope checks.]

### Release Management

[Specify release notes, compatibility communication, and rollback policy.]

## 28. Containerization Standards

### Docker

[Specify approved images, multi-stage builds, and runtime identity.]

### Container Security

[Specify least privilege, secret injection, and image scanning.]

### Resource Limits

[Specify memory/CPU limits and health behavior.]

## 29. Documentation Requirements

### README

[Specify user-visible feature summaries, setup, and links.]

### Architecture Documentation

[Specify diagrams, responsibilities, and data flows.]

### API Documentation

[Specify contracts, examples, errors, and compatibility.]

### Database Documentation

[Specify schema, migrations, retention, and recovery.]

### Deployment Documentation

[Specify configuration, external permissions, and verification.]

### ADRs

[Specify when architectural decisions need a recorded rationale.]

## 30. Code Review Checklist

[List required correctness, security, scope, testing, accessibility, and documentation checks.]

## 31. Definition of Done

[Define acceptance outcomes, passing checks, review, and documentation updates.]

## 32. AI Agent Output Requirements

Report the implemented behavior, every changed path, exact validation commands
and their results, documentation updates, and remaining blockers. Distinguish
tests actually run from suggested commands. Request approval before expanding
the task's allowed files.

## 33. Prohibited Practices

### Anti-Patterns

[Specify prohibited shortcuts, unrelated refactors, and silent fallbacks.]

### Security Violations

[Specify forbidden secret exposure, unauthorized access, and unsafe execution.]

### Architecture Violations

[Specify disallowed layer dependencies and duplicated business rules.]

### Performance Violations

[Specify unbounded queries/work, N+1 calls, and blocking hot paths.]

## 34. Naming Conventions

### Projects

[Specify project naming.]

### Namespaces

[Specify namespace naming.]

### Classes

[Specify class naming.]

### Interfaces

[Specify interface naming.]

### Records

[Specify record naming and usage.]

### Enums

[Specify enum naming and compatibility rules.]

### DTOs

[Specify contract naming.]

### Endpoints

[Specify route and operation naming.]

## 35. File and Folder Organization

[Specify where implementation, tests, and documentation belong.]

## 36. Source Control Standards

### Git Workflow

[Specify branches, review, and integration workflow.]

### Branch Naming

[Specify prefixes and task references.]

### Commit Message Format

[Specify the repository's existing commit convention.]

### Pull Requests

[Specify task/spec binding, required checks, reviewers, and scope amendments.]

## 37. Deployment Architecture

[Describe environments, services, networks, data stores, and trust boundaries.]

## 38. Non-Functional Requirements

### Scalability

[Specify workload and scaling constraints.]

### Reliability

[Specify failure handling and recovery objectives.]

### Maintainability

[Specify compatibility, ownership, and change isolation.]

### Availability

[Specify measurable availability goals and monitoring.]

### Accessibility

[Specify accessibility standard and verification journeys.]

## 39. Sample Reference Implementation

[Link approved repository paths/commits; do not copy unrelated third-party code.]

## 40. Project-Specific Instructions

### Business Rules

[State domain-specific rules and acceptance examples.]

### Domain Constraints

[State invariants and data restrictions.]

### Custom Requirements

[State additional approved requirements.]

### Future Enhancements

[List explicitly out-of-scope ideas; these do not authorize current task changes.]
