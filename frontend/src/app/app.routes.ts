import { Routes } from '@angular/router';
import { adminGuard } from './features/auth/admin.guard';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/home/home').then((module) => module.Home) },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/auth-form').then((module) => module.AuthForm),
    data: { mode: 'register' },
  },
  {
    path: 'login',
    loadComponent: () => import('./features/auth/auth-form').then((module) => module.AuthForm),
    data: { mode: 'login' },
  },
  {
    path: 'admin/event-types',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./features/event-types/event-types').then((module) => module.EventTypes),
  },
  { path: '**', redirectTo: '' },
];
