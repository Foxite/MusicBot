using System;
using System.Linq.Expressions;
using AngleSharp.Dom.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IkIheMusicBot {
	public static class DependencyUtil {
		public static IOptions<T> GetOptions<T>(this IServiceProvider isp) where T : class => isp.GetRequiredService<IOptions<T>>();
	}
}
