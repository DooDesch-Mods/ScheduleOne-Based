# Changelog

All notable changes to Based are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and this
project adheres to Semantic Versioning.

## [1.1.4] - 2026-11-06

### Fixed
- The corner by the north window of the finished Sweatshop expansion can be built on when another mod removes the
  sink there.

### Changed
- Based asks for MapExpansions 1.2.0, which this fix needs. With an older one Based still runs, the corner stays
  blocked and `MelonLoader/Latest.log` warns.
- The Thunderstore page links the required mods on Nexus Mods and GitHub, for when Thunderstore can't install them.

## [1.1.3] - 2026-11-05

### Fixed
- Employees who were upstairs in the Sweatshop expansion when the save loaded no longer get stuck there, so the handler
  empties the loading dock again.
- Employees no longer end up on the Sweatshop roof while waiting. They wait in the yard by the outside stairs.

### Changed
- Based asks for MapExpansions 1.1.0, which both fixes need. With an older one Based still runs and warns in
  `MelonLoader/Latest.log`.
- Based stays off with an S1API older than 3.2.1, and Mrs. Ming says so.
## [1.1.2] - 2026-11-05

### Changed
- Based asks for MapExpansions 1.0.4, which fixes employees ignoring storage and drying racks in the new part of the Sweatshop.
- With an older MapExpansions Based still runs and warns in `MelonLoader/Latest.log`.
- When a required mod is missing, Mrs. Ming says what is wrong with it: not installed, in the wrong folder or too old.

## [1.1.1] - 2026-11-05

### Fixed
- Co-op: a friend joining your game no longer hangs on "Syncing" when a project folder hangs on one of your walls.

## [1.1.0] - 2026-11-02

### Added
- Loading dock for the Sweatshop: after the expansion the project folder offers it for $1,500, built in one night of sleep.
- The dock sits in the yard behind the restaurant. The Sweatshop now shows up in the Deliveries app and the van parks there.
- Employees can unload the van at the dock with a packager route.
- Order further stages in the project folder itself. Mrs. Ming only handles the first expansion.
- Lost the folder? Ray at Ray's Realty sells a copy of the building plans for $200 cash, for each property with a Based expansion.

## [1.0.1] - 2026-11-02

### Changed

- Thunderstore and Nexus Mods may now review the mod files: the license names their package review as an exception.

## [1.0.0] - 2026-11-02

### Added
- Expand the Sweatshop: ask Mrs. Ming for more space, $1,000 for the rights, $3,000 for construction.
- A crew builds it over four nights: breakthrough, frame, roof and interior, each one visible in the world.
- A project folder shows how far the work is: open it from your hand or hang it on a wall.
- 42 m² more on the fourth morning: place stations and storage in it, and hire 3 employees instead of 1.
- Co-op: the player who clicks pays and the host decides. Every player needs the mod and the same NPC packs.
