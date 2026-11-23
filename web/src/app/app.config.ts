import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { authInterceptor } from './auth/auth.interceptor';
import { freshnessInterceptor } from './shared/data-freshness.service';
import { ScrollRestoreService } from './shared/scroll-restore.service';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideEmsUi(),
    provideHttpClient(withInterceptors([authInterceptor, freshnessInterceptor])),
    provideRouter(routes),
    // Back returns to where the page was scrolled once its figures have loaded; a new page opens at the top.
    provideAppInitializer(() => void inject(ScrollRestoreService)),
  ],
};
