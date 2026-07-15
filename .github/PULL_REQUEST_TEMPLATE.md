## Summary

<!-- What changed and why (1–3 bullets). Link issues with Fixes #N when applicable. -->

-

## Architecture check

- [ ] Layers respected (decide ≠ transmit ≠ serialize)
- [ ] No new `Network/` catch-all; files land in `Packets/` / `Protocol/` / `Session/` / domain folders
- [ ] Packets do not reference `Player` / `World` / `Server`
- [ ] No freeze-list additions without ADR in `docs/decisions.md`

## Test plan

- [ ] `dotnet test zenith.sln`
- [ ] Manual Bedrock smoke (if UI/join/world): describe steps

<!-- e.g. two clients place/break; mobile join after fragment fix; MOTD count after quit -->

## Notes

<!-- Risk, follow-ups, screenshots/logs if useful. -->
