# Versioned Sujiko Fixture

`synthetic-sujiko-newspaper.jpg` was provided by the repository owner on 27 August 2026 as an AI-generated replacement fixture for public regression testing. It contains a fictional newspaper-style Sujiko puzzle with quadrant totals 20, 11, 24, and 23 and given cells 3 and 7. It contains no real publication branding, personal data, or confidential source material.

The selected JPEG was inspected before commit:

- dimensions: 448 x 506 pixels;
- SHA-256: `4429A427DF01DE20BE381A4B58DB5307B4B8663C2951F9B68B7A1DBC3D6AB633`;
- no EXIF profile or GPS metadata; and
- no embedded text metadata beyond normal JPEG/JFIF structure.

`SujikoAssetRegressionTests` protects the dimensions, hash, missing EXIF profile, known puzzle values, and optional live-provider path. If the fixture is intentionally replaced, inspect the new image, update this note and the permanent assertions together, and keep the replacement free of personal metadata and third-party source material.
