# patch-target-audit

Audits Harmony and reflection targets that normal MemberRef metadata cannot see.
Rules cover string-named methods/properties/fields, bare-lookup ambiguity, and
specific calls that a transpiler expects to find in target IL.

```sh
dotnet run --project tools/patch-target-audit/audit.csproj -- \
  /path/to/sts2.dll tools/patch-target-audit/sts2-targets.tsv
```

Required missing/ambiguous rules exit 1. Optional rules represent a documented
null/skip/log compatibility branch: they are reported as `OPTIONAL_MISSING` but
do not fail the audit. The manifest covers `sts2.dll`; Harmony, GodotSharp and
third-party BaseLib targets live in other assemblies and remain runtime-gated.

Run `tests/run.sh` in the pinned .NET SDK container to verify missing targets,
ambiguous bare lookups, IL call-shape checks, and optional degradation behavior.
