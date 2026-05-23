import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

/**
 * The app root is just a shell — router-outlet plus the toast and
 * confirm-dialog hosts. We test that it instantiates and renders those
 * three slots; feature behaviour is covered (or will be) by per-feature
 * specs.
 */
describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the router outlet and shared hosts', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('router-outlet')).not.toBeNull();
    expect(html.querySelector('app-toast-host')).not.toBeNull();
    expect(html.querySelector('app-confirm-dialog-host')).not.toBeNull();
  });
});
