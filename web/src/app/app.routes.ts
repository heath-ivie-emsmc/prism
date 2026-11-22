import { Routes } from '@angular/router';
import { anonymousOnlyGuard, authGuard } from './auth/auth.guard';
import { ShellComponent } from './shell/shell.component';
import { unsavedChangesGuard } from './saved-views/unsaved-changes.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () => import('./dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'dashboard/net-cash',
        loadComponent: () => import('./breakdown/net-cash-breakdown.component').then((m) => m.NetCashBreakdownComponent),
      },
      {
        path: 'dashboard/net-cash/:group',
        loadComponent: () => import('./payer-mix/payer-mix.component').then((m) => m.PayerMixComponent),
      },
      {
        path: 'dashboard/needs-attention',
        title: 'Needs attention · Prism',
        loadComponent: () => import('./attention/needs-attention.component').then((m) => m.NeedsAttentionComponent),
      },
      {
        path: 'saved-views',
        title: 'Saved views · Prism',
        loadComponent: () => import('./saved-views/saved-views-list.component').then((m) => m.SavedViewsListComponent),
      },
      {
        path: 'saved-views/new',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./saved-views/view-builder.component').then((m) => m.ViewBuilderComponent),
      },
      {
        path: 'saved-view-invitations/redeem',
        title: 'Shared view · Prism',
        loadComponent: () => import('./saved-views/redeem-invitation.component').then((m) => m.RedeemInvitationComponent),
      },
      {
        path: 'saved-views/:id',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./saved-views/view-builder.component').then((m) => m.ViewBuilderComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
