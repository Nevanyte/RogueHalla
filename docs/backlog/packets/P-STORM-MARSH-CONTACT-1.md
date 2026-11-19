# P-STORM-MARSH-CONTACT-1: Match painted marsh contact effects to delivered variants

- **Outcome:** Owner-approved Storm Ink marsh presentation fix. Certified visually shallow water responds with a shallow wake and ripple. Painted mud uses the established mud contact response.
- **Exclusive scope:** `PlagueGround.cs`, new `StormInkMarshContactEditModeTests.cs` and its metadata, and this packet.
- **Contracts:** The existing Storm Ink ground eligibility and asset availability govern both pigment and contacts. Only eligible walkable Plague floor with no effective terrain uses variant 7 (shallow water) or 8 (mud). True terrain such as Bog retains priority. Preserve legacy fallback and dressed suppression of procedural noise water and mud. No simulation, wire, ground art or movement changes.
- **Red oracle:** The authored, eligible map resolves delivered variant 7 to Grass before the fix. A separate ineligible case checks fallback. Coverage also checks effective Bog precedence, wall and foreign-region rejection, and unchanged procedural counts.
- **Validation:** Focused EditMode oracle, related dressed-map and cypress tests, then normal-scale graphics replay crossing real classified shallow water and Bog on the unmodified certified map.
