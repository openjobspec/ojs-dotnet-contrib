## Summary

Describe the integration change and its framework impact.

## Verification

- [ ] `dotnet restore --locked-mode`
- [ ] `dotnet build -c Release --no-restore --warnaserror`
- [ ] `dotnet test -c Release --no-build`
- [ ] Package and consumer-smoke checks pass
- [ ] Public extension methods and configuration compatibility are preserved
