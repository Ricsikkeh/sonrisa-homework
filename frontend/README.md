# Frontend

Angular 22 and Angular Material 22, using standalone components, lazy feature routing, signals, and the Angular CLI's Vitest runner.

## Commands

Run from `frontend/` with Node.js 24 and npm:

```sh
npm ci
npm start
npm test -- --watch=false
npm run build
```

The development server listens at `http://localhost:4200`. Its proxy forwards `/api/**` to `http://localhost:5200`, stripping the `/api` prefix. `GET /api/health` checks API liveness, not database readiness.

`src/app/features/home/home.facade.ts` owns HTTP access and status state. The home component handles presentation. Material uses a locally bundled theme and system fonts, with no external asset requests.

Only the scaffold is implemented: there are no account, event, subscription, admin, or notification screens yet.

See the [repository README](../README.md) for the backend/database setup and [validation record](../docs/VALIDATION.md) for red/green evidence and limitations.
