import { Routes } from '@angular/router';
import { Login } from './pages/login/login';
import { Register } from './pages/register/register';
import { Account } from './pages/account/account';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'meetings',
    pathMatch: 'full'
  },
  {
    path: 'login',
    component: Login
  },
  {
    path: 'register',
    component: Register
  },
  {
    path: 'meetings',
    loadComponent: () => import('./pages/meetings/meetings').then(page => page.Meetings),
    canActivate: [authGuard]
  },
  {
    path: 'meetings/new',
    loadComponent: () => import('./pages/meeting-form/meeting-form').then(page => page.MeetingForm),
    canActivate: [authGuard]
  },
  {
    path: 'meetings/:id/edit',
    loadComponent: () => import('./pages/meeting-form/meeting-form').then(page => page.MeetingForm),
    canActivate: [authGuard]
  },
  {
    path: 'account',
    component: Account,
    canActivate: [authGuard]
  },
  {
    path: '**',
    redirectTo: 'meetings'
  }
];
