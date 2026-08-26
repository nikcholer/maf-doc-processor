# Natural Desk Fixture Generation Notes

`sources/natural-desk-three-documents.png` was generated specifically for this repository on 26 August 2026 using OpenAI's built-in image-generation tool. It contains fictional business names and no people, real brands, addresses, payment details, signatures, barcodes, or other personal information.

The selected output was inspected visually before it was added. The normalized bounds in `manifest.json` describe the visible paper edges in the committed image and are expected detection targets, not hidden generation metadata.

## Prompt

```text
Use case: photorealistic-natural
Asset type: non-confidential document-region detection fixture for a public software repository
Primary request: create a realistic top-down photograph of an ordinary uncluttered wooden desk holding exactly three separate paper documents: two narrow shop receipts and one small handwritten grocery shopping list.
Scene/backdrop: neutral light wood desktop, no other personal objects.
Subject: three complete pieces of white paper, all fully inside the frame with clear space between them; the receipts should be visibly different lengths and the shopping list should be wider and handwritten.
Style/medium: natural high-resolution smartphone photograph, realistic paper texture and slight imperfect rotation, sharp enough to see document boundaries.
Composition/framing: landscape, straight top-down view, each document separated and easy to bound; no overlap; generous desk margin around every page.
Lighting/mood: soft even daylight, minimal shadow, no glare.
Text: use only obviously fictional generic headings such as "NORTH STAR CAFE", "MEADOW MARKET", and "WEEKLY SHOPPING"; small body text may be generic.
Constraints: exactly three paper documents; no people, hands, bank cards, addresses, phone numbers, QR codes, barcodes, signatures, real brands, logos, watermarks, or sensitive/personal information. Keep all paper edges clearly visible and do not crop any document.
```

The other committed PNGs are deterministic renderings of the project-authored SVG files under `source-definitions`. Run `render-fixtures.ps1` on Windows to render those definitions with headless Microsoft Edge. The generated desk photograph is deliberately excluded from that renderer and is preserved as a fixed, hash-checked fixture.
