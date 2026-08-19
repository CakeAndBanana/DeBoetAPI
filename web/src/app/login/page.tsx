import { Suspense } from 'react';
import { Center, Loader } from '@mantine/core';
import { LoginForm } from './login-form';

export default function LoginPage() {
  return (
    <Center h="100vh" p="md">
      {/* LoginForm reads the ?next= param, which forces client rendering — Next requires
          that to sit behind a Suspense boundary so the rest of the page can prerender. */}
      <Suspense fallback={<Loader />}>
        <LoginForm />
      </Suspense>
    </Center>
  );
}
