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
        path: 'pessoas/:id',
        canMatch: [permissionGuard('people.read')],
        loadComponent: () =>
          import('./features/employees/employee-form.page').then((m) => m.EmployeeFormPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
