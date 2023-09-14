import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LoginPage } from '../pages/LoginPage';
import { mockFetch, renderWithProviders } from '../test/utils';

it('shows the problem details message on bad credentials', async () => {
  mockFetch(() => ({ status: 401, body: { title: 'Invalid credentials', detail: 'Email or password is incorrect.', status: 401 } }));
  renderWithProviders(<LoginPage />, { route: '/signin', path: '/signin' });

  await userEvent.type(screen.getByLabelText('Email'), 'casey@example.com');
  await userEvent.type(screen.getByLabelText('Password'), 'wrong');
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.');
});

it('stores tokens and sends admins to the dashboard', async () => {
  mockFetch((url) => url === '/api/auth/login' ? {
    body: { accessToken: 'a', refreshToken: 'r', expiresAt: '', refreshTokenExpiresAt: '',
      user: { id: '1', email: 'admin@shopstream.local', fullName: 'Avery Admin', role: 'Admin', createdAt: '' } },
  } : undefined);
  renderWithProviders(<LoginPage />, { route: '/signin', path: '/signin' });

  await userEvent.type(screen.getByLabelText('Email'), 'admin@shopstream.local');
  await userEvent.type(screen.getByLabelText('Password'), 'Demo@12345');
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

  expect(await screen.findByText('admin home')).toBeInTheDocument();
  expect(JSON.parse(localStorage.getItem('shop-stream.auth')!).accessToken).toBe('a');
});
