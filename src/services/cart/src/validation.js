'use strict';

const { z } = require('zod');
const { HttpError } = require('./errors');

const MAX_QUANTITY = 20;

const productId = z.string().uuid({ message: 'productId must be a UUID' });

const schemas = {
  addItem: z.object({
    productId,
    quantity: z.number().int().min(1).max(MAX_QUANTITY).default(1),
  }),
  updateItem: z.object({
    quantity: z.number().int().min(0).max(MAX_QUANTITY),
  }),
  productParam: z.object({ productId }),
};

function validate(schema, source = 'body') {
  return (req, res, next) => {
    const result = schema.safeParse(req[source]);
    if (!result.success) {
      const errors = {};
      for (const issue of result.error.issues) {
        const key = issue.path.join('.') || source;
        (errors[key] ||= []).push(issue.message);
      }
      return next(new HttpError(400, 'One or more validation errors occurred.', undefined, { errors }));
    }
    req[source] = result.data;
    return next();
  };
}

module.exports = { schemas, validate, MAX_QUANTITY };
