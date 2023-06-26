'use strict';

class HttpError extends Error {
  constructor(status, title, detail, extra) {
    super(detail || title);
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.extra = extra;
  }
}

const notFound = (detail) => new HttpError(404, 'Resource not found', detail);
const badRequest = (detail) => new HttpError(400, 'Bad request', detail);
const unauthorized = (detail) => new HttpError(401, 'Unauthorized', detail);
const unavailable = (detail) => new HttpError(503, 'Dependency unavailable', detail);

function sendProblem(req, res, status, title, detail, extra = {}) {
  res
    .status(status)
    .type('application/problem+json')
    .send({
      type: `https://httpstatuses.io/${status}`,
      title,
      status,
      detail,
      instance: req.originalUrl,
      correlationId: req.correlationId,
      ...extra,
    });
}

// eslint-disable-next-line no-unused-vars
function errorHandler(err, req, res, next) {
  if (err instanceof HttpError) {
    return sendProblem(req, res, err.status, err.title, err.detail, err.extra);
  }
  if (err.type === 'entity.parse.failed') {
    return sendProblem(req, res, 400, 'Malformed JSON body');
  }
  req.log?.error({ err }, 'Unhandled error');
  return sendProblem(req, res, 500, 'An unexpected error occurred');
}

module.exports = { HttpError, notFound, badRequest, unauthorized, unavailable, errorHandler, sendProblem };
