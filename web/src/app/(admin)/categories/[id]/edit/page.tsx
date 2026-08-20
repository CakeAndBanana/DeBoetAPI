import { notFound } from 'next/navigation';
import { Stack, Title } from '@mantine/core';
import { ApiError } from '@/lib/problem';
import { flatten, getCategory, getTree } from '@/lib/categories';
import { CategoryForm } from '../../category-form';

export default async function EditCategoryPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const detail = await getCategory(id).catch((error) => {
    if (error instanceof ApiError && error.status === 404) notFound();
    throw error;
  });

  const options = flatten(await getTree());

  return (
    <Stack>
      <Title order={2}>Edit {detail.category.name}</Title>
      <CategoryForm
        id={id}
        options={options}
        initial={{
          name: detail.category.name,
          slug: detail.category.slug,
          metaTitle: detail.category.metaTitle ?? '',
          metaDescription: detail.category.metaDescription ?? '',
          sortOrder: Number(detail.category.sortOrder),
          isActive: detail.category.isActive,
          isMenu: detail.category.isMenu,
        }}
      />
    </Stack>
  );
}
