# Prism runs on the latest Angular and overrides the shared library's peer dependencies

`@ems-management-consultants/ui` 0.1.0 is built against Angular and Angular Material 19.2 and declares `^19.2.0` peer dependencies. Its primary consumer is still on that version, so the library will not be upgraded for Prism. Prism runs on the latest Angular (22.3) and installs the library with an npm `overrides` block in `web/package.json` that points the library's Angular peers at Prism's own versions.

A spike on 2026-11-20 showed the library builds and renders on Angular 22.3 and Material 22.3: the nav bar, select (including opening its panel), calendar and pagination work in `web/src/app/library-spike.spec.ts`. Components the spike did not exercise (dialogs, record picker, checklist select, tables, date adapter) are unverified. Add a test for each one before first use. If a component breaks, report it to the library owners rather than patching it in Prism.

**Fallback:** if the override becomes unworkable, pin Prism to Angular 19.2 until the library moves, then remove the override. Remove the override as soon as the library publishes a release with Angular 22 peers.
