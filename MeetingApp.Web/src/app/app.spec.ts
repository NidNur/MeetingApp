import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';

describe('Application navigation', () => {
  const user = signal<{
    userId: number;
    firstName: string;
    lastName: string;
    expiresAtUtc: string;
  } | null>(null);
  let logout: jasmine.Spy;

  beforeEach(async () => {
    user.set(null);
    logout = jasmine.createSpy('logout');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: AuthService, useValue: { user, logout } }]
    }).compileComponents();
  });

  it('offers login and registration to a guest', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const links = Array.from(element.querySelectorAll('nav a'));
    expect(links.map(link => link.getAttribute('href'))).toEqual(['/login', '/register']);
    expect(element.querySelector('nav button')).toBeNull();
  });

  it('replaces guest links with account and logout controls after authentication', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    user.set({ userId: 7, firstName: 'Lale', lastName: 'Deneme', expiresAtUtc: '2099-01-01T00:00:00Z' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const links = Array.from(element.querySelectorAll('nav a'));
    expect(links.map(link => link.getAttribute('href'))).toEqual(['/meetings', '/account']);
    expect(element.querySelector('nav button')?.textContent).toContain('Çıkış yap');
  });

  it('connects the logout button to session termination', () => {
    user.set({ userId: 7, firstName: 'Lale', lastName: 'Deneme', expiresAtUtc: '2099-01-01T00:00:00Z' });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    (element.querySelector('nav button') as HTMLButtonElement).click();
    expect(logout).toHaveBeenCalledTimes(1);
  });
});
