# N.I.N.A. Target History — prototype V0.1

Purpose: build a live catalogue from N.I.N.A. Advanced Sequencer JSON files.

Implemented:
- recursive scan of the chosen sequences folder
- automatic rescan when JSON files are created/changed/renamed/deleted
- parsing of `DeepSkyObjectContainer`
- target name, RA/Dec and position angle retained internally
- `ExposureInfoList` aggregation by filter
- same target can occur in several sequence files; exposure totals are accumulated
- alphabetical list + instant search
- All / In progress / Finished filtering
- Finished state and AstroBin URL persisted outside the N.I.N.A. sequence files
- AstroBin link opens in the default browser
- malformed/in-use JSON does not stop the catalogue

Important V0.1 limitation:
The catalogue/parser is implemented, but the final N.I.N.A. `IDockableVM` registration and
Framing Assistant navigation adapter are intentionally isolated/not hard-coded here because
those host contracts must match the exact N.I.N.A. 3.3 build installed on the test machine.
The current official plugin kit supports `--dock-panel`; wire `TargetHistoryView` and
`TargetHistoryViewModel` into the generated dock-panel class for that exact SDK build.

Recommended bootstrap on the N.I.N.A. development PC:

    dotnet new install NINA.Plugin.Templates
    dotnet new nina-plugin -n NINA.TargetHistory --dock-panel --displayName "Target History"
    dotnet build

Then replace the generated example panel content with the files in this archive.

Data rule:
Sequence JSON is always the source of truth. A changed sequence replaces its previous
contribution on the next rebuild; it is never blindly appended, so exposure time cannot
double merely because the file was edited.

Next integration:
- double-click Target -> Framing Assistant centered on stored RA/Dec
- use stored PositionAngle
- draw one camera rectangle using the active N.I.N.A. profile
- optional historical-field overlay later
