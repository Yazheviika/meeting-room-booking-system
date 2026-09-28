import { Routes } from '@angular/router';
import { Login } from './auth/login/login';
import { Register } from './auth/register/register';
import { authGuard, adminGuard } from './auth/guards';
import { Rooms } from './rooms/rooms';
import { Admin } from './admin/admin';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'rooms' },
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  { path: 'rooms', component: Rooms, canActivate: [authGuard] },
  { path: 'admin', component: Admin, canActivate: [authGuard, adminGuard] },
  { path: '**', redirectTo: 'rooms' },
];
