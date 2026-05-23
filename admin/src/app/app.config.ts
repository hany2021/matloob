import {
  APP_INITIALIZER,
  ApplicationConfig,
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withViewTransitions } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideOAuthClient } from 'angular-oauth2-oidc';

import { routes } from './app.routes';
import { AuthService } from './core/auth/auth.service';
import { authInterceptor } from './core/http/auth.interceptor';
import { problemDetailsInterceptor } from './core/http/problem-details.interceptor';

/**
 * Run OIDC discovery + try-login BEFORE the first route activation so
 * authenticated routes don't race the redirect handling.
 */
function provideAuthBoot(): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: APP_INITIALIZER,
      multi: true,
      useFactory: () => {
        const auth = inject(AuthService);
        auth.configure();
        return () => auth.loadDiscoveryAndTryLogin();
      },
    },
  ]);
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withViewTransitions()),
    provideHttpClient(
      withInterceptors([authInterceptor, problemDetailsInterceptor]),
    ),
    provideOAuthClient(),
    provideAuthBoot(),
  ],
};
