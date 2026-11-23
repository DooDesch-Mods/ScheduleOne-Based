# Changelog

All notable changes to Based are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and this
project adheres to Semantic Versioning.

## [1.3.1] - 2026-11-23

### Fixed
- Ray's menu shows "I want to expand one of my properties." and "I lost my building plans." again when another mod
  removed them. Based names that mod in the log.
- Bungalow sunroom: wall items (lamps, shelves, signs) can be hung on the house wall inside the sunroom.
- Bungalow storey: the roof's graffiti spot sits on top of the new storey and can be sprayed again.
- Storage Unit: the construction crew reaches all its work spots in the loft, on the walkway and in the south room.

## [1.3.0] - 2026-11-23

### Added
- Bungalow expansion: Ray sells a building permit for $1,500 and four builds, one at a time.
- Second storey with a balcony and an outside stair: $6,000, built over 6 nights.
- Sunroom on the west side for $4,000 over 3 nights, a deck on stilts down the slope for $2,000 over 2 nights.
- Double carport over the drive with a second loading bay for $1,500 over 2 nights; rain stays out from under it.
- A crew you can watch builds it night by night; this site has no foreman to tip.

### Changed
- MapExpansions 1.5.0 or newer is now required.

### Fixed
- Wall items like the Modern Wall Lamp can be placed on the upstairs walls of the Storage Unit and the Bungalow storey.

## [1.2.5] - 2026-11-20

### Fixed
- Hiring into the finished Sweatshop with Actually Working Employees installed no longer throws errors or loses
  employee spots.

## [1.2.4] - 2026-11-19

### Fixed
- Mushroom beds, spawn stations and drying racks placed upstairs in the Storage Unit before 1.2.3 load again.
- A world already saved with 1.2.3 no longer holds them. Only a copy of the save from before that update brings them
  back.

## [1.2.3] - 2026-11-19

### Fixed
- The upstairs rooms of the Storage Unit count as one build area: racks and other items fit across the lines
  between them.
- Items already placed upstairs keep their spot when you load an older save.

## [1.2.2] - 2026-11-18

### Fixed
- Ray's office menu keeps the building plans and expansion entries when another mod swaps in its own copy of his
  estate agent talk.
- If Ray's entries are still missing, the log says once per load which check left them out.

## [1.2.1] - 2026-11-17

### Fixed
- Storage Unit: workers waiting for work gather at the fire barrel and the outside stair instead of standing in
  the hall.
- Sweatshop workers no longer stand still and stop working when another mod allows more employees than Based plans
  for.

## [1.2.0] - 2026-11-17

### Added
- Storage Unit expansion: Ray sells a loft with an outside stair, an east room, a south room and a second loading bay.
- Each build takes several nights, and a crew you can watch does the work.
- Tip the foreman in cash or product to cut the construction trash.

### Changed
- Construction workers take short breaks during working hours.
- MapExpansions 1.4.0 or newer is now required.

## [1.1.6] - 2026-11-11

### Fixed
- The project folder works again with S1API 3.2.1: you can take it, buy a copy from Ray and hang it on a wall.
  Ray no longer asks you to make room in your pockets when there is room.

## [1.1.5] - 2026-11-09

### Fixed

- Without S1API installed, Mrs. Ming says which mod is missing instead of Based failing silently.
- With MapExpansions older than 1.1.0, Based stays off and names the update instead of the Ming expansion failing.

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
