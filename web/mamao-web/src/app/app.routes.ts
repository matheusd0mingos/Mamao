import { Routes } from '@angular/router';
import { authGuard, permissionGuard } from './core/auth/auth.guard';

/** Lazy loading por feature: o bundle inicial carrega casca + visao geral. */
export const routes: Routes = [
  {
    path: 'entrar',
    loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'cadastrar-empresa',
    loadComponent: () =>
      import('./features/auth/register-company.page').then((m) => m.RegisterCompanyPage),
  },
  {
    path: 'esqueci-minha-senha',
    loadComponent: () =>
      import('./features/auth/forgot-password.page').then((m) => m.ForgotPasswordPage),
  },
  {
    // O link do e-mail cai aqui, com email e token na query string.
    path: 'redefinir-senha',
    loadComponent: () =>
      import('./features/auth/reset-password.page').then((m) => m.ResetPasswordPage),
  },
  {
    // Publica: quem chega aqui ainda nao tem conta.
    path: 'aceitar-convite',
    loadComponent: () =>
      import('./features/auth/accept-invite.page').then((m) => m.AcceptInvitePage),
  },
  {
    path: '',
    canMatch: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'inicio' },
      {
        path: 'inicio',
        loadComponent: () =>
          import('./features/dashboard/dashboard.page').then((m) => m.DashboardPage),
      },
      {
        path: 'pessoas',
        canMatch: [permissionGuard('people.read')],
        loadComponent: () =>
          import('./features/employees/employees.page').then((m) => m.EmployeesPage),
      },
      {
        path: 'pessoas/nova',
        canMatch: [permissionGuard('people.write')],
        loadComponent: () =>
          import('./features/employees/employee-form.page').then((m) => m.EmployeeFormPage),
      },
      {
        path: 'disponibilidade',
        canMatch: [permissionGuard('people.read')],
        loadComponent: () =>
          import('./features/availability/availability.page').then((m) => m.AvailabilityPage),
      },
      {
        path: 'calendario',
        canMatch: [permissionGuard('people.read')],
        loadComponent: () =>
          import('./features/availability/calendar.page').then((m) => m.CalendarPage),
      },
      {
        path: 'demandas',
        canMatch: [permissionGuard('work.read')],
        loadComponent: () => import('./features/work/work.page').then((m) => m.WorkPage),
      },
      {
        path: 'escala',
        canMatch: [permissionGuard('schedule.read')],
        loadComponent: () =>
          import('./features/missions/missions.page').then((m) => m.MissionsPage),
      },
      {
        path: 'acessos',
        canMatch: [permissionGuard('users.invite')],
        loadComponent: () =>
          import('./features/access/access.page').then((m) => m.AccessPage),
      },
      {
        path: 'estrutura',
        canMatch: [permissionGuard('people.read')],
        loadComponent: () =>
          import('./features/organization/organization.page').then((m) => m.OrganizationPage),
      },
      {
        // Antes de 'pessoas/:id': senao "importar" seria lido como um id de funcionario.
        path: 'pessoas/importar',
        canMatch: [permissionGuard('people.write')],
        loadComponent: () =>
          import('./features/employees/employee-import.page').then((m) => m.EmployeeImportPage),
      },
      {
        // Ver e a operacao comum; editar e a rara. Antes o nome levava direto ao
        // formulario, e formulario aberto por engano e formulario salvo por engano.
        path: 'pessoas/:id',
        canMatch: [permissionGuard('people.read')],
        loadComponent: () =>
          import('./features/employees/employee-profile.page').then((m) => m.EmployeeProfilePage),
      },
      {
        path: 'pessoas/:id/editar',
        canMatch: [permissionGuard('people.write')],
        loadComponent: () =>
          import('./features/employees/employee-form.page').then((m) => m.EmployeeFormPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
