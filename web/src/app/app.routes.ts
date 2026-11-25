import { Routes } from '@angular/router';
import { anonymousOnlyGuard, authGuard, entryGuard, internalGuard, onboardingGuard, welcomeGuard } from './auth/auth.guard';
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
    canActivateChild: [onboardingGuard],
    children: [
      { path: '', pathMatch: 'full', canActivate: [entryGuard], children: [] },
      {
        path: 'welcome',
        title: 'Welcome · Prism',
        canActivate: [welcomeGuard],
        loadComponent: () => import('./personas/onboarding.component').then((m) => m.OnboardingComponent),
      },
      {
        path: 'settings',
        title: 'Settings · Prism',
        loadComponent: () => import('./settings/settings.component').then((m) => m.SettingsComponent),
      },
      {
        path: 'settings/users',
        title: 'Users · Prism',
        loadComponent: () => import('./customers/provider-users.component').then((m) => m.ProviderUsersComponent),
      },
      {
        path: 'settings/questionnaire',
        title: 'Your questions · Prism',
        data: { retake: true },
        loadComponent: () => import('./personas/onboarding.component').then((m) => m.OnboardingComponent),
      },
      {
        // The home page: the user's Persona page, their own copy of it, or the standard dashboard.
        path: 'dashboard',
        loadComponent: () => import('./personas/home.component').then((m) => m.HomeComponent),
      },
      {
        path: 'dashboard/net-cash',
        loadComponent: () => import('./metrics/net-cash-breakdown/net-cash-breakdown.component').then((m) => m.NetCashBreakdownComponent),
      },
      {
        path: 'dashboard/net-cash/:group',
        loadComponent: () => import('./metrics/payer-mix/payer-mix.component').then((m) => m.PayerMixComponent),
      },
      {
        path: 'dashboard/metrics/payer-mix/:group/:payerKey',
        title: 'Payer breakdown · Prism',
        loadComponent: () => import('./metrics/payer-mix-detail/payer-mix-detail.component').then((m) => m.PayerMixDetailComponent),
      },
      {
        path: 'dashboard/metrics/:key',
        title: 'Metric · Prism',
        loadComponent: () => import('./metrics/metric-view/metric-view.component').then((m) => m.MetricViewComponent),
      },
      {
        path: 'dashboard/needs-attention',
        title: 'Needs attention · Prism',
        loadComponent: () => import('./attention/needs-attention.component').then((m) => m.NeedsAttentionComponent),
      },
      {
        // Every admin page sits inside one frame with the admin navigation.
        path: 'admin',
        canActivate: [internalGuard],
        loadComponent: () => import('./admin/admin-layout.component').then((m) => m.AdminLayoutComponent),
        children: [
          { path: '', title: 'Admin · Prism', loadComponent: () => import('./admin/admin-home.component').then((m) => m.AdminHomeComponent) },
          { path: 'customers', title: 'Customer users · Prism', loadComponent: () => import('./admin/admin-customers.component').then((m) => m.AdminCustomersComponent) },
          { path: 'personas', title: 'Personas · Prism', loadComponent: () => import('./admin/admin-personas.component').then((m) => m.AdminPersonasComponent) },
          { path: 'templates', title: 'View Templates · Prism', loadComponent: () => import('./admin/admin-templates.component').then((m) => m.AdminTemplatesComponent) },
          {
            path: 'templates/new',
            canDeactivate: [unsavedChangesGuard],
            loadComponent: () => import('./admin/template-editor.component').then((m) => m.TemplateEditorComponent),
          },
          {
            path: 'templates/:id',
            canDeactivate: [unsavedChangesGuard],
            loadComponent: () => import('./admin/template-editor.component').then((m) => m.TemplateEditorComponent),
          },
        ],
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
