'use strict';

const pino = require('pino');
const config = require('./config');

module.exports = pino({
  level: process.env.NODE_ENV === 'test' ? 'silent' : config.logLevel,
  base: { Service: 'cart' },
  messageKey: '@m',
  timestamp: () => `,"@t":"${new Date().toISOString()}"`,
  formatters: { level: (label) => ({ '@l': label }) },
});
