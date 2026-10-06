# Screenshots

All screenshots show a fictional demo library (generated covers, made-up titles and cast), not real content.

## Movies

The library grid with status, filters and sorting.

![Movies grid](assets/movies.png)

## Filtering

The Filter menu on the Movies grid combines filter groups: resolution, scan type, `.nfo` drift, codec, studio, genre, features and, under **Actor**, the cast's profile data. Selected groups show a count, the grid and the status tabs update as you go, and the × next to the Filter button clears everything at once.

Actor filters work on the profile data of the people in a movie's cast: cup size, height, bust, waist, hips, age and name. This shows movies whose cast includes someone with cup size C or D who is 163 cm or taller.

![Actor filters: cup size and height](assets/filter-actor-cup-height.png)

The menu with the Resolution, Genre and Features groups expanded: HD, the Drama and Romance genres, and movies that have scenes.

![Filter menu: resolution, genre and features](assets/filter-menu.png)

Codec and Studio, with the Height range slider of the Actor group.

![Filter menu: codec, studio and actor height](assets/filter-combined.png)

Cup size is a set of toggle buttons and Age is a range slider. Here cup size B or C and ages 20 to 25.

![Filter menu: actor cup size and age](assets/filter-actor-age.png)

Filters from different groups combine, so any mix of these narrows the grid further.

## r18.dev catalog

With the r18.dev source enabled, the Movies page's **All releases (r18.dev)** view browses the whole r18.dev catalog, newest first. Typing a code or title in the filter narrows it as you type. Here `LUMW-1` returns that studio's releases, each marked Missing (wanted), Got or untracked, with the status buttons above counting each state. Untracked releases can be added or searched for straight from the card.

![r18.dev catalog search](assets/r18dev-search.png)

## Movie detail

The whole page for a movie with scenes, highlights, apexes, favorites and extra images: the scene timeline with highlight and apex markers, highlight and scene cards, and the image row.

![Movie detail](assets/movie-detail-full.png)

## Trickplay and previews

Hovering the scene timeline on Movie detail previews the movie at that moment from its trickplay frames. Here the pointer is at 0:43, and the frame comes from the whole movie's trickplay, which has one frame every few seconds.

![Movie trickplay on the scene timeline](assets/movie-trickplay.png)

A highlight has its own, denser trickplay, generated for just its range. Hovering a highlight's marker below the timeline scrubs through it, so the frame is much closer to the exact moment.

![Dense trickplay for a highlight](assets/highlight-trickplay.png)

Hovering an apex marker pops up its own short preview clip, with its tags and time.

![Apex preview](assets/apex-preview.png)

## Scene editor

A movie's scenes, highlights and apexes as one tree next to the player, with tags and performers per scene.

![Scene editor](assets/scene-editor.png)

## Image viewer

The full-screen viewer for a movie's extra images and actor photos: previous/next, zoom, and the original file.

![Image viewer](assets/image-viewer.png)

## Review

Step through the library one movie at a time in the built-in player, then keep, snooze or blacklist it.

![Review](assets/review.png)

## Discover

New and upcoming releases from studio sites, grouped by studio, ready to add to your wanted list.

![Discover](assets/discover.png)

## Missing

Every tracked movie you don't own yet, in one list.

![Missing](assets/missing.png)

## Scenes

Scenes from the whole library, filterable by actor and tag, with Play all and Shuffle.

![Scenes wall](assets/scenes.png)

## Actors

Tracked actors with their photos and "needs attention" filters.

![Actors](assets/actors.png)

## Actor detail

An actor's profile (birth date, measurements, cup size, Japanese name), photo albums, movies, and every scene, highlight and apex they appear in.

![Actor detail](assets/actor-detail.png)

## Photo editor

Changing an actor's portrait: upload a file or import a URL, then crop with a square, free-form or 2:3 box and a zoom slider, with a live avatar preview. Save the crop, or upload the full image.

![Actor portrait editor](assets/photo-editor.png)

The same cropper is used for movie covers. Here a wide DVD cover is cropped to its front panel, with a preview of the result.

![Cover cropper](assets/cover-crop.png)

## Merging actors

Duplicate actors can be merged into one. The dialog suggests a target, summarizes what moves (movies, metadata, external IDs, cached photos), saves the duplicate's name as an alias of the target and can update the actor name in linked `.nfo` files.

![Merge actors dialog](assets/merge-dialog.png)

## Actor photos

Every actor photo in the library on one page, filterable by actor and album.

![Actor photos](assets/actor-photos.png)

## Tags

The tag library, with a review queue for newly discovered tags.

![Tags](assets/tags.png)

## Statistics

A library overview: status, resolution and codec breakdowns, top actors and tags.

![Statistics](assets/statistics.png)

## Tasks

Scheduled background jobs with run-now and history.

![Tasks](assets/tasks.png)
