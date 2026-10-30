import { Routes } from '@angular/router';

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
  { path: '**', redirectTo: '' },
];
