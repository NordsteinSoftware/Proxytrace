# Domain Validation

Domain entities are validated by Autofac on activation (`OnActivated` runs `Validator.ValidateObject`) and again before repository `Add`/`Update`. Override `Validate(ValidationContext)` and yield `base.Validate(...)` first. Use helpers from `Nordstein.Core.Common.Validation`:

```csharp
Validation.NotNullOrWhiteSpace(Name, nameof(Name))   // note: capital S in "WhiteSpace"
Validation.NotNull(SystemEndpoint, nameof(SystemEndpoint))
Validation.NotDefault(SomeGuid, nameof(SomeGuid))
Validation.InPast(CreatedAt, nameof(CreatedAt))
Validation.NotBefore(UpdatedAt, CreatedAt, nameof(UpdatedAt))
```

For referenced entities, cascade validation:
```csharp
foreach (var r in SystemEndpoint.Validate(validationContext)) yield return r;
```

## Writing a new check

A check returns a `ValidationResult` describing the failure, or `Validation.Success` when the value is
fine — never `ValidationResult.Success` directly.

That indirection is deliberate. The BCL represents success as a **null** `ValidationResult`, but
declares `IValidatableObject.Validate` to return a non-nullable element type, so every check has to
hand back a value the framework itself defines as null through a signature we cannot change. The
resulting suppression is concentrated on the single `Validation.Success` member — the one sanctioned
`!` in the repository (see the nullable-suppression rule in `CLAUDE.md`). Returning any genuinely
non-null "success" sentinel instead would be read by `Validator` as a validation *failure*.

For a value constrained to a closed set that isn't an enum (e.g. `User.Language` must be one of the
`SupportedLanguages`), validate membership explicitly:
```csharp
yield return Validation.NotNullOrWhiteSpace(Language);
if (!SupportedLanguages.IsSupported(Language))
    yield return new ValidationResult($"Language '{Language}' is not a supported UI language.", [nameof(Language)]);
```
